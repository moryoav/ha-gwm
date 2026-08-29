using System.Collections.Concurrent;
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
    private const string RussianPendingResultCode = "1000";
    private const string RussianIntermediateResultCode = "11";
    private const int DefaultMaxResultPolls = 18;
    private const int RussianMaxResultPolls = 60;
    private static readonly TimeSpan ResultPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinimumChargingWindow = TimeSpan.FromMinutes(5);

    private readonly AddonOptions _options;
    private readonly AddonStateStore _stateStore;
    private readonly GwmApiClientFactory _clientFactory;
    private readonly GwmAuthenticationService _authentication;
    private readonly RemoteCommandStore _store;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<RemoteCommandService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _commandQueues = new();

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

    private void QueueCommand(string vin, Func<Task> execute)
    {
        // Serialize per vehicle, not globally. The server already rejects a
        // command while another is still executing for that vehicle, so a global
        // queue would only slow down unrelated vehicles.
        var queue = _commandQueues.GetOrAdd(vin, _ => new SemaphoreSlim(1, 1));
        _ = Task.Run(
            async () =>
            {
                await queue.WaitAsync(_lifetime.ApplicationStopping);
                try
                {
                    await execute();
                }
                finally
                {
                    queue.Release();
                }
            },
            CancellationToken.None);
    }

    public RemoteCommandSnapshot EnqueueClimate(string vin, ClimateCommandRequest request)
    {
        EnsureRemoteCommandsAvailable();
        ValidateClimateRequest(request);
        var mode = request.Mode?.Trim().ToLowerInvariant();
        if (mode == "heat" && !IsChinaRegion)
        {
            throw new ArgumentException("Climate heating is currently available only for the experimental China region.", nameof(request));
        }

        var commandName = mode is null && !request.Temperature.HasValue ? "A/C run time" : "A/C";
        var command = _store.Create(vin, commandName);
        QueueCommand(command.Vin, () => ExecuteClimateAsync(command.Id, request, _lifetime.ApplicationStopping));
        return command;
    }

    public static void ValidateClimateRequest(ClimateCommandRequest request)
    {
        var mode = request.Mode?.Trim().ToLowerInvariant();
        if (mode is not null and not ("cool" or "heat" or "off" or "auto"))
        {
            throw new ArgumentException("Climate command mode must be 'cool', 'heat', 'auto', or 'off'.", nameof(request));
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
        QueueCommand(command.Vin, () => ExecuteLockAsync(command.Id, normalized == "lock", _lifetime.ApplicationStopping));
        return command;
    }

    public RemoteCommandSnapshot EnqueueWindowClose(string vin)
    {
        EnsureRemoteCommandsAvailable();
        var command = _store.Create(vin, "Window close");
        QueueCommand(command.Vin, () => ExecuteWindowCloseAsync(command.Id, _lifetime.ApplicationStopping));
        return command;
    }

    public RemoteCommandSnapshot EnqueueVehicleControl(string vin, VehicleControlCommandRequest request)
    {
        EnsureRemoteCommandsAvailable();
        if (!IsChinaRegion)
        {
            throw new RemoteCommandUnavailableException(
                "These experimental vehicle controls are currently available only for the China region.");
        }

        ValidateVin(vin);
        var action = request.Action?.Trim().ToLowerInvariant() ?? String.Empty;
        var commandName = action switch
        {
            "remote_start" => "Remote start",
            "remote_stop" => "Remote stop",
            "horn" => "Sound horn",
            "flash_lights" => "Flash lights",
            "horn_and_lights" => "Sound horn and flash lights",
            "tailgate_open" => "Tailgate open",
            "tailgate_close" => "Tailgate close",
            "sunroof_close" => "Sunroof close",
            "sunroof_tilt" => "Sunroof tilt",
            "sunroof_half" => "Sunroof half open",
            "sunroof_full" => "Sunroof fully open",
            "seat_heating_driver" => "Driver seat heating",
            "seat_heating_passenger" => "Passenger seat heating",
            "seat_ventilation_driver" => "Driver seat ventilation",
            "seat_ventilation_passenger" => "Passenger seat ventilation",
            "seat_heating_stop" => "Seat heating off",
            "seat_ventilation_stop" => "Seat ventilation off",
            "steering_wheel_heating" => "Steering wheel heating",
            "steering_wheel_heating_stop" => "Steering wheel heating off",
            "defrost_front" => "Front defrost",
            "defrost_front_stop" => "Front defrost off",
            "defrost_back" => "Rear defrost",
            "defrost_back_stop" => "Rear defrost off",
            "cabin_cleaning" => "Cabin cleaning",
            "comfort_warm" => "Comfort (warm)",
            "comfort_cool" => "Comfort (cool)",
            "comfort_last" => "Comfort (last)",
            "comfort_off" => "Comfort off",
            "battery_gun_heat" => "Battery pack heating (plugged in)",
            "battery_gun_heat_stop" => "Battery pack heating off (plugged in)",
            "battery_initiative_heat" => "Battery pack active heating",
            "battery_initiative_heat_stop" => "Battery pack active heating off",
            _ => throw new ArgumentException($"Unsupported China vehicle-control action '{request.Action}'.", nameof(request))
        };

        if (request.RunTimeMinutes is int runTime
            && !VehicleSnapshotMapper.IsValidOperationTime(runTime))
        {
            throw new ArgumentException("Remote-start run time must be a whole number from 5 to 30 minutes.", nameof(request));
        }

        var command = _store.Create(vin, commandName);
        QueueCommand(
            command.Vin,
            () => ExecuteVehicleControlAsync(
                command.Id,
                action,
                request.RunTimeMinutes,
                _lifetime.ApplicationStopping));
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
            Task<VehicleStatus>? statusTask = IsChinaRegion
                                              || (mode is null && request.Temperature.HasValue)
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

            if (mode is not null and not ("cool" or "heat" or "off" or "auto"))
            {
                _store.Update(id, "failed", $"{command.Name}: failed - unsupported mode '{request.Mode}'");
                return;
            }

            if (mode is "cool" or "heat" || request.Temperature.HasValue || request.OperationTimeMinutes.HasValue)
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
            var effectiveMode = mode ?? "cool";
            var sendCommand = IsChinaRegion
                ? RemoteCommandFactory.CreateChinaClimateCommand(
                    command.Vin,
                    SecurityPassword,
                    effectiveMode,
                    temperature,
                    operationTime,
                    currentlyOn)
                : RemoteCommandFactory.CreateClimateCommand(
                    command.Vin,
                    SecurityPassword,
                    switchOrder,
                    temperature,
                    operationTime,
                    IsRussianRegion);
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
            var request = RemoteCommandFactory.CreateLockCommand(
                command.Vin,
                SecurityPassword,
                lockVehicle,
                IsRussianRegion);
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
            var request = RemoteCommandFactory.CreateWindowCloseCommand(
                command.Vin,
                SecurityPassword,
                IsRussianRegion);
            await SendAndPollAsync(client, id, request, cancellationToken);
        }
        catch (Exception ex)
        {
            FailCommand(id, command.Name, ex);
        }
    }

    private async Task ExecuteVehicleControlAsync(
        string id,
        string action,
        int? runTimeMinutes,
        CancellationToken cancellationToken)
    {
        var command = _store.Get(id)!;
        try
        {
            var client = await AuthenticatedClientAsync(cancellationToken);
            var request = action switch
            {
                "remote_start" => RemoteCommandFactory.CreateChinaCommand(
                    command.Vin,
                    SecurityPassword,
                    ChinaRemoteCommandKind.EngineStart,
                    15,
                    runTimeMinutes ?? VehicleSnapshotMapper.DefaultOperationTimeMinutes),
                "remote_stop" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 16),
                "horn" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 19),
                "flash_lights" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 20),
                "horn_and_lights" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 5),
                "tailgate_open" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 17),
                "tailgate_close" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 18),
                "sunroof_close" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 28),
                // AutoAI reports closed as 0 and numbers the three opening positions from
                // fully open toward the vent position.
                "sunroof_full" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.SunroofOpen, 29, openAngle: 1),
                "sunroof_half" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.SunroofOpen, 29, openAngle: 2),
                "sunroof_tilt" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.SunroofOpen, 29, openAngle: 3),
                "seat_heating_driver" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 30),
                "seat_heating_passenger" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 31),
                "seat_ventilation_driver" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 32),
                "seat_ventilation_passenger" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 33),
                "seat_heating_stop" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 34),
                "seat_ventilation_stop" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 35),
                "steering_wheel_heating" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 36),
                "steering_wheel_heating_stop" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 37),
                "defrost_front" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 38),
                "defrost_front_stop" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 39),
                "defrost_back" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 40),
                "defrost_back_stop" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 41),
                "cabin_cleaning" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 42),
                "comfort_warm" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 43),
                "comfort_cool" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 44),
                "comfort_last" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 45),
                "comfort_off" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 46),
                // 电池包保温（frida 实测 2026-08-29，需 PIN）
                "battery_gun_heat" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 47),
                "battery_gun_heat_stop" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 48),
                "battery_initiative_heat" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 49),
                "battery_initiative_heat_stop" => RemoteCommandFactory.CreateChinaCommand(command.Vin, SecurityPassword, ChinaRemoteCommandKind.Common, 50),
                _ => throw new ArgumentException($"Unsupported China vehicle-control action '{action}'.")
            };
            await SendAndPollAsync(client, id, request, cancellationToken);
        }
        catch (Exception ex)
        {
            FailCommand(id, command.Name, ex);
        }
    }

    // Charging schedules use the authenticated account but no vehicle security PIN. A plan window
    // gates charging start/stop; clearing it reverts to charge-on-plug.
    // 智能预约充电（beantech）。不走命令追踪那套（那是 msgType=remote 的远控命令），
    // 这里就是一次设置读写，状态靠再读一次确认。
    public async Task<ChargingModeState> GetChargingModeAsync(string vin, CancellationToken cancellationToken)
    {
        ValidateVin(vin);
        var client = await AuthenticatedClientAsync(cancellationToken);
        var (enabled, startTime, endTime) = await client.GetBeanTechChargeSettingAsync(vin, cancellationToken);
        return new ChargingModeState
        {
            Enabled = enabled,
            StartTime = startTime,
            EndTime = endTime
        };
    }

    public RemoteCommandSnapshot SetChargingMode(string vin, bool enable)
    {
        EnsureChargingControlAvailable();
        ValidateVin(vin);
        var command = _store.Create(
            vin,
            enable ? "Smart scheduled charging on" : "Smart scheduled charging off");
        QueueCommand(
            command.Vin,
            () => ExecuteChargingModeAsync(command.Id, enable, _lifetime.ApplicationStopping));
        return command;
    }

    private async Task ExecuteChargingModeAsync(string id, bool enable, CancellationToken cancellationToken)
    {
        var command = _store.Get(id)!;
        try
        {
            var client = await AuthenticatedClientAsync(cancellationToken);
            _store.Update(id, "in_progress", $"{command.Name}: sending setting to GWM");
            var seqNo = await client.SetBeanTechChargingModeAsync(command.Vin, enable, cancellationToken);
            _store.Update(id, "in_progress", $"{command.Name}: accepted by GWM, waiting for result", seqNo);

            for (var attempt = 1; attempt <= DefaultMaxResultPolls; attempt++)
            {
                await Task.Delay(ResultPollInterval, cancellationToken);
                var (resultCode, resultMessage) = await client.GetBeanTechChargeResultAsync(
                    seqNo,
                    command.Vin,
                    cancellationToken);

                if (String.IsNullOrWhiteSpace(resultCode))
                {
                    _store.Update(
                        id,
                        "in_progress",
                        $"{command.Name}: waiting for result ({attempt}/{DefaultMaxResultPolls})",
                        seqNo);
                    continue;
                }

                // resultCode "2" = 远控指令待执行（还没落地），"0" = 成功。
                if (String.Equals(resultCode, "2", StringComparison.Ordinal))
                {
                    _store.Update(
                        id,
                        "in_progress",
                        $"{command.Name}: in progress ({attempt}/{DefaultMaxResultPolls}) - " +
                        $"{resultMessage} [{resultCode}]",
                        seqNo);
                    continue;
                }

                var succeeded = String.Equals(resultCode, "0", StringComparison.Ordinal);
                _store.Update(
                    id,
                    succeeded ? "completed" : "failed",
                    $"{command.Name}: {(succeeded ? "completed" : "failed")} - {resultMessage} [{resultCode}]",
                    seqNo,
                    resultCode,
                    resultMessage);
                return;
            }

            _store.Update(
                id,
                "failed",
                $"{command.Name}: failed - no result from the vehicle after " +
                $"{DefaultMaxResultPolls} polls",
                seqNo);
        }
        catch (Exception ex)
        {
            FailCommand(id, command.Name, ex);
        }
    }

    public async Task<ChargingInfos> GetChargingPlanAsync(string vin, CancellationToken cancellationToken)
    {
        ValidateVin(vin);
        var client = await AuthenticatedClientAsync(cancellationToken);
        return await client.GetChargingInfosAsync(vin, cancellationToken);
    }

    public async Task SetChargingPlanAsync(string vin, ChargingPlanRequest request, CancellationToken cancellationToken)
    {
        EnsureChargingControlAvailable();
        ValidateChargingPlanRequest(vin, request);

        var enable = request.Enable!.Value;
        var client = await AuthenticatedClientAsync(cancellationToken);
        var plan = new SetChargingPlan { Enable = enable, Vin = vin };
        if (enable)
        {
            plan.PlanType = request.PlanType ?? 0;
            plan.StartTime = request.StartTime?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            plan.EndTime = request.EndTime?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            plan.Weeks = request.Weeks ?? String.Empty;
        }

        await client.SetChargingPlanAsync(plan, cancellationToken);

        if (!enable)
        {
            await RemoveTrackedChargingPlanAsync(vin, CancellationToken.None);
            return;
        }

        var trackedPlan = new TrackedChargingPlan
        {
            PlanType = plan.PlanType!.Value,
            StartTime = request.StartTime!.Value,
            EndTime = request.EndTime!.Value,
            Weeks = plan.Weeks ?? String.Empty
        };

        // Capture the server-assigned plan id when possible. The write has already succeeded,
        // so a read-back failure must not turn the successful command into a user-visible error.
        try
        {
            var current = await client.GetChargingInfosAsync(vin, cancellationToken);
            trackedPlan.PlanId = current.ChargePlanList
                .FirstOrDefault(candidate => ChargingPlanMatches(candidate, trackedPlan))
                ?.PlanId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Charging plan was set, but its server id could not be confirmed");
        }

        // Persist ownership even if the HTTP request was canceled after GWM accepted the write.
        await _stateStore.UpdateAsync(
            state => state.ChargingPlansSetByAddon[vin] = trackedPlan,
            CancellationToken.None);
    }

    // If charging control is turned off, remove only plans that still match the exact plan the
    // add-on wrote. A plan replaced in the GWM app gets a different id or window and is preserved.
    public async Task ClearAddonChargingPlansIfDisabledAsync(CancellationToken cancellationToken)
    {
        if (_options.EnableChargingControl)
        {
            return;
        }

        var trackedPlans = await _stateStore.ReadAsync(
            state => state.ChargingPlansSetByAddon.ToArray(),
            cancellationToken);
        if (trackedPlans.Length == 0)
        {
            return;
        }

        var client = await AuthenticatedClientAsync(cancellationToken);
        foreach (var (vin, trackedPlan) in trackedPlans)
        {
            try
            {
                var current = await client.GetChargingInfosAsync(vin, cancellationToken);
                var matchingPlan = current.ChargePlanList
                    .FirstOrDefault(candidate => ChargingPlanMatches(candidate, trackedPlan));
                if (matchingPlan is null)
                {
                    if (current.ChargePlanList.Any(IsActiveChargingPlan))
                    {
                        _logger.LogInformation(
                            "Left a charging plan unchanged because it no longer matches the plan written by the add-on");
                    }

                    await RemoveTrackedChargingPlanAsync(vin, cancellationToken);
                    continue;
                }

                await client.SetChargingPlanAsync(new SetChargingPlan { Enable = false, Vin = vin }, cancellationToken);
                _logger.LogInformation("Cleared a leftover add-on charging plan because charging control is now disabled");
                await RemoveTrackedChargingPlanAsync(vin, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not inspect or clear a leftover charging plan; the next poll will retry");
            }
        }
    }

    internal static void ValidateChargingPlanRequest(string vin, ChargingPlanRequest request)
    {
        ValidateVin(vin);
        if (!request.Enable.HasValue)
        {
            throw new ArgumentException("Charging plan request must specify enable.", nameof(request));
        }

        if (!request.Enable.Value)
        {
            return;
        }

        if (request.PlanType is not null and not 0)
        {
            throw new ArgumentException("Only one-off charging plans (plan_type 0) are supported.", nameof(request));
        }

        if (!request.StartTime.HasValue || !request.EndTime.HasValue)
        {
            throw new ArgumentException("Enabled charging plans require both start_time and end_time.", nameof(request));
        }

        DateTimeOffset start;
        DateTimeOffset end;
        try
        {
            start = DateTimeOffset.FromUnixTimeMilliseconds(request.StartTime.Value);
            end = DateTimeOffset.FromUnixTimeMilliseconds(request.EndTime.Value);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ArgumentException("Charging plan times must be valid Unix timestamps in milliseconds.", nameof(request), ex);
        }

        if (end - start < MinimumChargingWindow)
        {
            throw new ArgumentException("Charging plan window must be at least 5 minutes.", nameof(request));
        }
    }

    internal static bool ChargingPlanMatches(ChargePlanItem candidate, TrackedChargingPlan trackedPlan)
    {
        if (!IsActiveChargingPlan(candidate)
            || trackedPlan.PlanId.HasValue && candidate.PlanId != trackedPlan.PlanId.Value)
        {
            return false;
        }

        return String.Equals(
                   candidate.PlanType,
                   trackedPlan.PlanType.ToString(System.Globalization.CultureInfo.InvariantCulture),
                   StringComparison.Ordinal)
               && candidate.StartTime == trackedPlan.StartTime
               && candidate.EndTime == trackedPlan.EndTime
               && String.Equals(candidate.Weeks ?? String.Empty, trackedPlan.Weeks, StringComparison.Ordinal);
    }

    private static bool IsActiveChargingPlan(ChargePlanItem plan) =>
        !String.IsNullOrWhiteSpace(plan.PlanType)
        && !String.Equals(plan.PlanType, "-1", StringComparison.Ordinal);

    private static void ValidateVin(string vin)
    {
        if (String.IsNullOrWhiteSpace(vin))
        {
            throw new ArgumentException("Vehicle VIN is required.", nameof(vin));
        }
    }

    private Task RemoveTrackedChargingPlanAsync(string vin, CancellationToken cancellationToken) =>
        _stateStore.UpdateAsync(
            state => state.ChargingPlansSetByAddon.Remove(vin),
            cancellationToken);

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

        var maxResultPolls = IsRussianRegion ? RussianMaxResultPolls : DefaultMaxResultPolls;
        RemoteCtrlResultT5? lastRussianIntermediateResult = null;
        for (var attempt = 1; attempt <= maxResultPolls; attempt++)
        {
            await Task.Delay(ResultPollInterval, cancellationToken);
            var results = await client.GetRemoteCtrlResultAsync(request.SeqNo, request.Vin, cancellationToken);
            var result = SelectRemoteCommandResult(results, request, IsRussianRegion);

            if (result is null)
            {
                _store.Update(
                    id,
                    "in_progress",
                    $"{command.Name}: waiting for vehicle result ({attempt}/{maxResultPolls})",
                    request.SeqNo);
                continue;
            }

            var isPending = IsPendingRemoteCommandResult(result, IsRussianRegion);
            var isRussianIntermediate = IsRussianRegion && RussianIntermediateResultCode.Equals(
                result.ResultCode,
                StringComparison.Ordinal);
            if (isRussianIntermediate)
            {
                lastRussianIntermediateResult = result;
            }

            var state = isPending || isRussianIntermediate
                ? "in_progress"
                : IsSuccessfulRemoteCommandResult(result) ? "completed" : "failed";
            var status = isRussianIntermediate
                ? $"{command.Name}: waiting for final vehicle result ({attempt}/{maxResultPolls}) " +
                  $"[{result.ResultCode}]"
                : FormatRemoteCommandResult(
                    command.Name,
                    result,
                    attempt,
                    IsRussianRegion,
                    maxResultPolls);
            _store.Update(
                id,
                state,
                status,
                request.SeqNo,
                result.ResultCode,
                result.ResultMsg);

            if (!isPending && !isRussianIntermediate)
            {
                return;
            }
        }

        var finalResultMessage = String.IsNullOrWhiteSpace(lastRussianIntermediateResult?.ResultMsg)
            ? "no message"
            : lastRussianIntermediateResult.ResultMsg;
        var finalDetails = lastRussianIntermediateResult is null
            ? String.Empty
            : $" Last GWM result: {finalResultMessage} [{lastRussianIntermediateResult.ResultCode}].";
        _store.Update(
            id,
            "timeout",
            $"{command.Name}: timed out waiting for vehicle result after " +
            $"{maxResultPolls * ResultPollInterval.TotalSeconds:0} seconds.{finalDetails}",
            request.SeqNo,
            lastRussianIntermediateResult?.ResultCode,
            lastRussianIntermediateResult?.ResultMsg);
    }

    private void EnsureRemoteCommandsAvailable()
    {
        if (!_options.EnableRemoteCommands)
        {
            throw new RemoteCommandUnavailableException("Remote commands are disabled in the add-on configuration.");
        }

        if (!IsChinaRegion && String.IsNullOrWhiteSpace(_options.SecurityPin))
        {
            throw new RemoteCommandUnavailableException("Remote commands require security_pin in the add-on configuration.");
        }
    }

    // Charging control is a separate, explicit opt-in from remote commands and needs no PIN.
    private void EnsureChargingControlAvailable()
    {
        if (!_options.EnableChargingControl)
        {
            throw new RemoteCommandUnavailableException("Charging control is disabled. Set enable_charging_control in the add-on configuration.");
        }
    }

    private string SecurityPassword => IsChinaRegion
        ? _options.BeantechEncryptedSecurityPin ?? String.Empty // beantech 填加密后的 securityPwd（setPasswordEncryptionForBB 结果）
        : new CheckSecurityPassword(_options.SecurityPin!).Md5Hash;

    private bool IsRussianRegion =>
        String.Equals(_options.Region, "rus", StringComparison.OrdinalIgnoreCase);

    private bool IsChinaRegion =>
        String.Equals(_options.Region, "cn", StringComparison.OrdinalIgnoreCase);

    private void FailCommand(string id, string commandName, Exception exception)
    {
        var message = exception is GwmApiException gwmException
            ? $"{gwmException.Message} [{gwmException.Code}]"
            : exception.Message;
        _store.Update(id, "failed", $"{commandName}: failed - {message}");
        _logger.LogError(exception, "Remote command {CommandId} failed", id);
    }

    internal static string FormatRemoteCommandResult(
        string commandName,
        RemoteCtrlResultT5 result,
        int attempt,
        bool isRussianRegion = false,
        int maxResultPolls = DefaultMaxResultPolls)
    {
        var resultCode = String.IsNullOrWhiteSpace(result.ResultCode) ? "unknown" : result.ResultCode;
        var resultMsg = String.IsNullOrWhiteSpace(result.ResultMsg) ? "no message" : result.ResultMsg;
        if (IsPendingRemoteCommandResult(result, isRussianRegion))
        {
            return $"{commandName}: in progress ({attempt}/{maxResultPolls}) - waiting for vehicle result [{resultCode}]";
        }

        var status = IsSuccessfulRemoteCommandResult(result) ? "completed" : "failed";
        return $"{commandName}: {status} - {resultMsg} [{resultCode}]";
    }

    internal static RemoteCtrlResultT5? SelectRemoteCommandResult(
        IReadOnlyCollection<RemoteCtrlResultT5?>? results,
        SendCmd request,
        bool isRussianRegion)
    {
        var usableResults = results?.Where(static result => result is not null).Cast<RemoteCtrlResultT5>().ToArray()
                            ?? Array.Empty<RemoteCtrlResultT5>();
        if (!isRussianRegion)
        {
            return usableResults.FirstOrDefault(x => String.Equals(
                       x.HwCommandId,
                       request.SeqNo,
                       StringComparison.OrdinalIgnoreCase))
                   ?? usableResults.FirstOrDefault();
        }

        var expectedRemoteType = ExpectedRemoteType(request);
        var exactSequenceCandidates = usableResults.Where(x => String.Equals(
                x.HwCommandId,
                request.SeqNo,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var exactCommandCandidates = String.IsNullOrWhiteSpace(expectedRemoteType)
            ? exactSequenceCandidates
            : exactSequenceCandidates.Where(x => String.Equals(
                    x.RemoteType,
                    expectedRemoteType,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var missingSequenceCandidates = String.IsNullOrWhiteSpace(expectedRemoteType)
            ? Array.Empty<RemoteCtrlResultT5>()
            : usableResults.Where(x => String.IsNullOrWhiteSpace(x.HwCommandId)
                                       && String.Equals(
                                           x.RemoteType,
                                           expectedRemoteType,
                                           StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var remoteTypeCandidates = String.IsNullOrWhiteSpace(expectedRemoteType)
            ? Array.Empty<RemoteCtrlResultT5>()
            : usableResults.Where(x => String.Equals(
                    x.RemoteType,
                    expectedRemoteType,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var candidates = exactCommandCandidates.Length > 0
            ? exactCommandCandidates
            : exactSequenceCandidates.Length > 0
                ? exactSequenceCandidates
                : missingSequenceCandidates.Length > 0
                    ? missingSequenceCandidates
                    : remoteTypeCandidates.Length > 0
                        ? remoteTypeCandidates
                        : String.IsNullOrWhiteSpace(expectedRemoteType)
                            ? usableResults
                            : Array.Empty<RemoteCtrlResultT5>();

        return candidates.FirstOrDefault(IsSuccessfulRemoteCommandResult)
               ?? candidates.FirstOrDefault(x => IsPendingRemoteCommandResult(x, isRussianRegion: true))
               ?? candidates.FirstOrDefault(x => RussianIntermediateResultCode.Equals(
                   x.ResultCode,
                   StringComparison.Ordinal))
               ?? candidates.FirstOrDefault();
    }

    internal static bool IsPendingRemoteCommandResult(
        RemoteCtrlResultT5 result,
        bool isRussianRegion)
    {
        return PendingResultCode.Equals(result.ResultCode, StringComparison.Ordinal)
               || isRussianRegion && RussianPendingResultCode.Equals(
                   result.ResultCode,
                   StringComparison.Ordinal);
    }

    private static string? ExpectedRemoteType(SendCmd request)
    {
        if (request.Instructions?.X04 is not null)
        {
            return "0x04";
        }
        if (request.Instructions?.X05 is not null)
        {
            return "0x05";
        }
        if (request.Instructions?.X08 is not null)
        {
            return "0x08";
        }

        return null;
    }

    private static bool IsSuccessfulRemoteCommandResult(RemoteCtrlResultT5 result)
    {
        return "0".Equals(result.ResultCode, StringComparison.Ordinal)
               || "6".Equals(result.ResultCode, StringComparison.Ordinal)
               || "Success".Equals(result.ResultMsg, StringComparison.OrdinalIgnoreCase);
    }
}
