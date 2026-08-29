using GwmOra.Addon.Configuration;
using GwmOra.Addon.Models;
using GwmOra.Addon.RemoteCommands;
using libgwmapi.DTO.Vehicle;

namespace GwmOra.Addon.Gwm;

public sealed class GwmVehicleService
{
    private readonly AddonOptions _options;
    private readonly AddonStateStore _stateStore;
    private readonly GwmApiClientFactory _clientFactory;
    private readonly GwmAuthenticationService _authentication;
    private readonly RemoteCommandStore _remoteCommandStore;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private VehicleSnapshot[] _vehicles = Array.Empty<VehicleSnapshot>();

    public GwmVehicleService(
        AddonOptions options,
        AddonStateStore stateStore,
        GwmApiClientFactory clientFactory,
        GwmAuthenticationService authentication,
        RemoteCommandStore remoteCommandStore)
    {
        _options = options;
        _stateStore = stateStore;
        _clientFactory = clientFactory;
        _authentication = authentication;
        _remoteCommandStore = remoteCommandStore;
    }

    public DateTimeOffset? LastRefresh { get; private set; }
    public string? LastError { get; private set; }
    public bool Authenticated { get; private set; }
    public bool VerificationRequired { get; private set; }

    public VehiclesResponse GetVehicles()
    {
        foreach (var vehicle in _vehicles)
        {
            vehicle.CommandStatus = _remoteCommandStore.GetLastStatus(vehicle.Vin);
        }

        return new VehiclesResponse
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Region = _options.Region.Trim().ToLowerInvariant(),
            RemoteCommandsEnabled = RemoteCommandsAvailable,
            SecurityPinConfigured = String.Equals(_options.Region, "cn", StringComparison.OrdinalIgnoreCase)
                ? !String.IsNullOrWhiteSpace(_options.BeantechEncryptedSecurityPin)
                : !String.IsNullOrWhiteSpace(_options.SecurityPin),
            ChargingControlEnabled = _options.EnableChargingControl,
            Vehicles = _vehicles
        };
    }

    public HealthResponse GetHealth()
    {
        return new HealthResponse
        {
            Status = VerificationRequired ? "verification_required" : LastError is null ? (LastRefresh.HasValue ? "ok" : "starting") : "error",
            Configured = true,
            Authenticated = Authenticated,
            VerificationRequired = VerificationRequired,
            VehicleCount = _vehicles.Length,
            RemoteCommandsEnabled = _options.EnableRemoteCommands,
            SecurityPinConfigured = String.Equals(_options.Region, "cn", StringComparison.OrdinalIgnoreCase)
                ? !String.IsNullOrWhiteSpace(_options.BeantechEncryptedSecurityPin)
                : !String.IsNullOrWhiteSpace(_options.SecurityPin),
            ChargingControlEnabled = _options.EnableChargingControl,
            PollIntervalSeconds = _options.PollIntervalSeconds,
            LastRefresh = LastRefresh,
            LastError = LastError
        };
    }

    public async Task RefreshNowAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var client = _clientFactory.Create(_options, _stateStore.State);
            await _authentication.EnsureAuthenticatedAsync(client, cancellationToken);
            Authenticated = true;

            var vehicles = await client.AcquireVehiclesAsync(cancellationToken);
            var snapshots = new List<VehicleSnapshot>(vehicles.Length);
            foreach (var vehicle in vehicles)
            {
                var statusTask = client.GetLastVehicleStatusAsync(vehicle.Vin, cancellationToken);
                var basicsTask = client.GetVehicleBasicsInfoOrDefaultAsync(vehicle.Vin, cancellationToken);
                await Task.WhenAll(statusTask, basicsTask);

                bool? insertGunKeepWarm = null;
                bool? activeKeepWarm = null;
                int? acTemperature = null;
                string? latestRemoteRecordMsg = null;
                if (String.Equals(vehicle.BelongPlatform?.Trim(), "beantech", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var (gun, active) = await client.GetBeanTechSwitchStatusAsync(vehicle.Vin, cancellationToken);
                        insertGunKeepWarm = gun;
                        activeKeepWarm = active;
                    }
                    catch (Exception)
                    {
                        // 保温状态读取失败时保持 null，不阻断整次刷新。
                    }

                    try
                    {
                        acTemperature = await client.GetBeanTechAirConditionerTemperatureAsync(vehicle.Vin, cancellationToken);
                    }
                    catch (Exception)
                    {
                        // 空调温度读取失败时保持 null，回退到默认值。
                    }

                    try
                    {
                        latestRemoteRecordMsg = await client.GetBeanTechLatestRemoteRecordAsync(vehicle.Vin, cancellationToken);
                    }
                    catch (Exception)
                    {
                        // 远控记录读取失败时保持 null。
                    }
                }

                snapshots.Add(VehicleSnapshotMapper.Map(
                    vehicle,
                    await statusTask,
                    await basicsTask,
                    RemoteCommandsAvailable,
                    _remoteCommandStore.GetLastStatus(vehicle.Vin),
                    ChargingControlAvailable(vehicle),
                    insertGunKeepWarm,
                    activeKeepWarm,
                    acTemperature,
                    latestRemoteRecordMsg));
            }

            _vehicles = snapshots.ToArray();
            LastRefresh = DateTimeOffset.UtcNow;
            LastError = null;
            VerificationRequired = false;
        }
        catch (GwmVerificationRequiredException ex)
        {
            LastError = ex.Message;
            Authenticated = false;
            VerificationRequired = true;
            throw;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Authenticated = false;
            VerificationRequired = false;
            throw;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private bool RemoteCommandsAvailable =>
        _options.EnableRemoteCommands
        && (String.Equals(_options.Region, "cn", StringComparison.OrdinalIgnoreCase)
            || !String.IsNullOrWhiteSpace(_options.SecurityPin));

    // 中国区两个平台的充电控制模型不同，但都支持：navinfo 走 charging plan 时间窗，
    // beantech 走 charge/setting 的 chargingMode 开关（frida 实测 2026-08-29 实现）。
    private bool ChargingControlAvailable(Vehicle vehicle) =>
        _options.EnableChargingControl
        && (!String.Equals(_options.Region, "cn", StringComparison.OrdinalIgnoreCase)
            || String.Equals(vehicle.BelongPlatform?.Trim(), "navinfo", StringComparison.OrdinalIgnoreCase)
            || String.Equals(vehicle.BelongPlatform?.Trim(), "beantech", StringComparison.OrdinalIgnoreCase));
}
