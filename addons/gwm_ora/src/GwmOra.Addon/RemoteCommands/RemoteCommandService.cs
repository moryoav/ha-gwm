using GwmOra.Addon.Configuration;
using GwmOra.Addon.Gwm;
using GwmOra.Addon.Models;
using libgwmapi;
using libgwmapi.DTO.UserAuth;
using libgwmapi.DTO.Vehicle;

namespace GwmOra.Addon.RemoteCommands;

public sealed class RemoteCommandService
{
    private const string PendingResultCode = "2000";
    private const int MaxResultPolls = 18;
    private static readonly TimeSpan ResultPollInterval = TimeSpan.FromSeconds(5);

    private readonly AddonOptions _options;
    private readonly AddonStateStore _stateStore;
    private readonly GwmApiClientFactory _clientFactory;
    private readonly GwmAuthenticationService _authentication;
    private readonly RemoteCommandStore _store;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<RemoteCommandService> _logger;

    public RemoteCommandService(
        AddonOptions options,
        AddonStateStore stateStore,
        GwmApiClientFactory clientFactory,
        GwmAuthenticationService authentication,
        RemoteCommandStore store,
        IHostApplicationLifetime lifetime,
        ILogger<RemoteCommandService> logger)
    {
        _options = options;
        _stateStore = stateStore;
        _clientFactory = clientFactory;
        _authentication = authentication;
        _store = store;
        _lifetime = lifetime;
        _logger = logger;
    }

    public RemoteCommandSnapshot? Get(string id) => _store.Get(id);

    public RemoteCommandSnapshot EnqueueClimate(string vin, ClimateCommandRequest request)
    {
        EnsureRemoteCommandsAvailable();
        ValidateClimateRequest(request);
        var mode = request.Mode?.Trim().ToLowerInvariant();

        var commandName = mode is null && !request.Temperature.HasValue ? "A/C run time" : "A/C";
        var command = _store.Create(vin, commandName);
        _ = Task.Run(() => ExecuteClimateAsync(command.Id, request, _lifetime.ApplicationStopping), CancellationToken.None);
        return command;
    }

    public static void ValidateClimateRequest(ClimateCommandRequest request)
    {
        var mode = request.Mode?.Trim().ToLowerInvariant();
        if (mode is not null and not ("cool" or "off"))
        {
            throw new ArgumentException("Climate command mode must be 'cool' or 'off'.", nameof(request));
        }

        if (request.OperationTimeMinutes is int operationTime
            && !VehicleSnapshotMapper.IsValidOperationTime(operationTime))
        {
            throw new ArgumentException("Climate run time must be a whole number from 5 to 30 minutes.", nameof(request));
        }

        if (mode is null && !request.Temperature.HasValue && !request.OperationTimeMinutes.HasValue)
        {
            throw new ArgumentException("Climate command requires a mode, temperature, or run time.", nameof(request));
        }
    }

    public static bool ShouldSendClimateCommand(ClimateCommandRequest request, bool currentlyOn)
    {
        return !String.IsNullOrWhiteSpace(request.Mode)
               || (request.Temperature.HasValue && currentlyOn);
    }

    public RemoteCommandSnapshot EnqueueLock(string vin, LockCommandRequest request)
    {
        EnsureRemoteCommandsAvailable();
        var normalized = request.Action.Trim().ToLowerInvariant();
        if (normalized is not ("lock" or "unlock"))
        {
            throw new ArgumentException("Lock command action must be 'lock' or 'unlock'.", nameof(request));
        }

        var command = _store.Create(vin, normalized == "lock" ? "Door lock" : "Door unlock");
        _ = Task.Run(() => ExecuteLockAsync(command.Id, normalized == "lock", _lifetime.ApplicationStopping), CancellationToken.None);
        return command;
    }

    public RemoteCommandSnapshot EnqueueWindowClose(string vin)
    {
        EnsureRemoteCommandsAvailable();
        var command = _store.Create(vin, "Window close");
        _ = Task.Run(() => ExecuteWindowCloseAsync(command.Id, _lifetime.ApplicationStopping), CancellationToken.None);
        return command;
    }

    private async Task ExecuteClimateAsync(string id, ClimateCommandRequest request, CancellationToken cancellationToken)
    {
        var command = _store.Get(id)!;
        try
        {
            var client = await AuthenticatedClientAsync(cancellationToken);
            _store.Update(id, "in_progress", $"{command.Name}: loading current settings");

            var mode = request.Mode?.Trim().ToLowerInvariant();
            var runTimeOnly = mode is null && !request.Temperature.HasValue;
            var basicsTask = client.GetVehicleBasicsInfoOrDefaultAsync(command.Vin, cancellationToken);
            Task<VehicleStatus>? statusTask = mode is null && request.Temperature.HasValue
                ? client.GetLastVehicleStatusAsync(command.Vin, cancellationToken)
                : null;
            if (statusTask is null)
            {
                await basicsTask;
            }
            else
            {
                await Task.WhenAll(statusTask, basicsTask);
            }

            var basics = await basicsTask;
            var currentlyOn = statusTask is not null
                              && (await statusTask).Items?.FirstOrDefault(x => x.Code == "2202001")?.Value?.ToString() == "1";
            int temperature;
            if (runTimeOnly)
            {
                if (!VehicleSnapshotMapper.TryGetValidTemperature(
                        basics.Config?.AirConditionerTemperature,
                        out temperature))
                {
                    _store.Update(
                        id,
                        "failed",
                        $"{command.Name}: failed - current A/C temperature is unavailable; no settings were changed");
                    return;
                }
            }
            else
            {
                temperature = VehicleSnapshotMapper.NormalizeTemperature(
                    request.Temperature?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? basics.Config?.AirConditionerTemperature,
                    22);
            }

            var operationTime = request.OperationTimeMinutes
                                ?? VehicleSnapshotMapper.NormalizeOperationTime(
                                    basics.Config?.AirConditionerStatusTime,
                                    VehicleSnapshotMapper.DefaultOperationTimeMinutes);

            if (mode is not null and not ("cool" or "off"))
            {
                _store.Update(id, "failed", $"{command.Name}: failed - unsupported mode '{request.Mode}'");
                return;
            }

            if (mode == "cool" || request.Temperature.HasValue || request.OperationTimeMinutes.HasValue)
            {
                _store.Update(id, "in_progress", $"{command.Name}: updating vehicle defaults");
                await client.ModifyVehicleRemoteCtlInfoAsync(
                    RemoteCommandFactory.CreateClimateDefaults(command.Vin, temperature, operationTime),
                    cancellationToken);
            }

            if (!ShouldSendClimateCommand(request, currentlyOn))
            {
                var status = runTimeOnly
                    ? $"{command.Name}: saved; applies to the next A/C command"
                    : $"{command.Name}: saved; A/C is off so no remote command was sent";
                _store.Update(id, "completed", status);
                return;
            }

            var switchOrder = mode == "off" ? "0" : "1";
            var sendCommand = RemoteCommandFactory.CreateClimateCommand(
                command.Vin,
                SecurityPassword,
                switchOrder,
                temperature,
                operationTime);
            await SendAndPollAsync(client, id, sendCommand, cancellationToken);
        }
        catch (Exception ex)
        {
            FailCommand(id, command.Name, ex);
        }
    }

    private async Task ExecuteLockAsync(string id, bool lockVehicle, CancellationToken cancellationToken)
    {
        var command = _store.Get(id)!;
        try
        {
            var client = await AuthenticatedClientAsync(cancellationToken);
            var request = RemoteCommandFactory.CreateLockCommand(command.Vin, SecurityPassword, lockVehicle);
            await SendAndPollAsync(client, id, request, cancellationToken);
        }
        catch (Exception ex)
        {
            FailCommand(id, command.Name, ex);
        }
    }

    private async Task ExecuteWindowCloseAsync(string id, CancellationToken cancellationToken)
    {
        var command = _store.Get(id)!;
        try
        {
            var client = await AuthenticatedClientAsync(cancellationToken);
            var request = RemoteCommandFactory.CreateWindowCloseCommand(command.Vin, SecurityPassword);
            await SendAndPollAsync(client, id, request, cancellationToken);
        }
        catch (Exception ex)
        {
            FailCommand(id, command.Name, ex);
        }
    }

    private async Task<GwmApiClient> AuthenticatedClientAsync(CancellationToken cancellationToken)
    {
        var client = _clientFactory.Create(_options, _stateStore.State);
        await _authentication.EnsureAuthenticatedAsync(client, cancellationToken);
        return client;
    }

    private async Task SendAndPollAsync(GwmApiClient client, string id, SendCmd request, CancellationToken cancellationToken)
    {
        var command = _store.Get(id)!;
        _store.Update(id, "in_progress", $"{command.Name}: sending command to GWM");
        await client.SendCmdAsync(request, cancellationToken);
        _store.Update(id, "in_progress", $"{command.Name}: accepted by GWM, waiting for vehicle result", request.SeqNo);

        for (var attempt = 1; attempt <= MaxResultPolls; attempt++)
        {
            await Task.Delay(ResultPollInterval, cancellationToken);
            var results = await client.GetRemoteCtrlResultAsync(request.SeqNo, request.Vin, cancellationToken);
            var result = results.FirstOrDefault(x => String.Equals(x.HwCommandId, request.SeqNo, StringComparison.OrdinalIgnoreCase))
                         ?? results.FirstOrDefault();

            if (result is null)
            {
                _store.Update(
                    id,
                    "in_progress",
                    $"{command.Name}: waiting for vehicle result ({attempt}/{MaxResultPolls})",
                    request.SeqNo);
                continue;
            }

            var state = PendingResultCode.Equals(result.ResultCode, StringComparison.Ordinal) ? "in_progress" :
                IsSuccessfulRemoteCommandResult(result) ? "completed" : "failed";
            _store.Update(
                id,
                state,
                FormatRemoteCommandResult(command.Name, result, attempt),
                request.SeqNo,
                result.ResultCode,
                result.ResultMsg);

            if (!PendingResultCode.Equals(result.ResultCode, StringComparison.Ordinal))
            {
                return;
            }
        }

        _store.Update(
            id,
            "timeout",
            $"{command.Name}: timed out waiting for vehicle result after {MaxResultPolls * ResultPollInterval.TotalSeconds:0} seconds",
            request.SeqNo);
    }

    private void EnsureRemoteCommandsAvailable()
    {
        if (!_options.EnableRemoteCommands)
        {
            throw new RemoteCommandUnavailableException("Remote commands are disabled in the add-on configuration.");
        }

        if (String.IsNullOrWhiteSpace(_options.SecurityPin))
        {
            throw new RemoteCommandUnavailableException("Remote commands require security_pin in the add-on configuration.");
        }
    }

    private string SecurityPassword => new CheckSecurityPassword(_options.SecurityPin!).Md5Hash;

    private void FailCommand(string id, string commandName, Exception exception)
    {
        var message = exception is GwmApiException gwmException
            ? $"{gwmException.Message} [{gwmException.Code}]"
            : exception.Message;
        _store.Update(id, "failed", $"{commandName}: failed - {message}");
        _logger.LogError(exception, "Remote command {CommandId} failed", id);
    }

    internal static string FormatRemoteCommandResult(string commandName, RemoteCtrlResultT5 result, int attempt)
    {
        var resultCode = String.IsNullOrWhiteSpace(result.ResultCode) ? "unknown" : result.ResultCode;
        var resultMsg = String.IsNullOrWhiteSpace(result.ResultMsg) ? "no message" : result.ResultMsg;
        if (PendingResultCode.Equals(result.ResultCode, StringComparison.Ordinal))
        {
            return $"{commandName}: in progress ({attempt}/{MaxResultPolls}) - waiting for vehicle result [{resultCode}]";
        }

        var status = IsSuccessfulRemoteCommandResult(result) ? "completed" : "failed";
        return $"{commandName}: {status} - {resultMsg} [{resultCode}]";
    }

    private static bool IsSuccessfulRemoteCommandResult(RemoteCtrlResultT5 result)
    {
        return "0".Equals(result.ResultCode, StringComparison.Ordinal)
               || "6".Equals(result.ResultCode, StringComparison.Ordinal)
               || "Success".Equals(result.ResultMsg, StringComparison.OrdinalIgnoreCase);
    }
}
