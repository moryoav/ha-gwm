using GwmOra.Addon.Configuration;
using GwmOra.Addon.Models;
using GwmOra.Addon.RemoteCommands;

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
            RemoteCommandsEnabled = RemoteCommandsAvailable,
            SecurityPinConfigured = !String.IsNullOrWhiteSpace(_options.SecurityPin),
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
            SecurityPinConfigured = !String.IsNullOrWhiteSpace(_options.SecurityPin),
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

                snapshots.Add(VehicleSnapshotMapper.Map(
                    vehicle,
                    await statusTask,
                    await basicsTask,
                    RemoteCommandsAvailable,
                    _remoteCommandStore.GetLastStatus(vehicle.Vin)));
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
        _options.EnableRemoteCommands && !String.IsNullOrWhiteSpace(_options.SecurityPin);
}
