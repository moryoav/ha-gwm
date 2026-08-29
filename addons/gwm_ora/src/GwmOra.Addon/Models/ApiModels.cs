namespace GwmOra.Addon.Models;

public sealed class HealthResponse
{
    public string Status { get; init; } = "starting";
    public bool Configured { get; init; }
    public bool Authenticated { get; init; }
    public bool VerificationRequired { get; init; }
    public int VehicleCount { get; init; }
    public bool RemoteCommandsEnabled { get; init; }
    public bool SecurityPinConfigured { get; init; }
    public bool ChargingControlEnabled { get; init; }
    public int PollIntervalSeconds { get; init; }
    public DateTimeOffset? LastRefresh { get; init; }
    public string? LastError { get; init; }
}

public sealed class VehiclesResponse
{
    public int ApiVersion { get; init; } = 1;
    public string Region { get; init; } = String.Empty;
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool RemoteCommandsEnabled { get; init; }
    public bool SecurityPinConfigured { get; init; }
    public bool ChargingControlEnabled { get; init; }
    public IReadOnlyList<VehicleSnapshot> Vehicles { get; init; } = Array.Empty<VehicleSnapshot>();
}

public sealed class VehicleSnapshot
{
    public string Vin { get; init; } = String.Empty;
    public string? Platform { get; init; }
    public string Name { get; init; } = String.Empty;
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public LocationSnapshot? Location { get; init; }
    public TimestampSnapshot Timestamps { get; init; } = new();
    public VehicleCapabilities Capabilities { get; init; } = new();
    public VehicleValues Values { get; init; } = new();
    public ClimateSnapshot Climate { get; init; } = new();
    public string CommandStatus { get; set; } = "No remote command has run yet";
    public IReadOnlyDictionary<string, RawItemSnapshot> RawItems { get; init; } = new Dictionary<string, RawItemSnapshot>();
}

public sealed class LocationSnapshot
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

public sealed class TimestampSnapshot
{
    public DateTimeOffset? AcquisitionTime { get; init; }
    public DateTimeOffset? UpdateTime { get; init; }
    public DateTimeOffset LastRefresh { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class VehicleCapabilities
{
    public bool RemoteCommands { get; init; }
    public bool ChargingControl { get; init; }
}

public sealed class VehicleValues
{
    public double? Soc { get; init; }
    public double? RangeKm { get; init; }
    public double? FuelLevelL { get; init; }
    public double? FuelRangeKm { get; init; }
    public double? RemainingChargingTimeMin { get; init; }
    public double? Soce { get; init; }
    public double? TirePressureFrontLeftKpa { get; init; }
    public double? TirePressureFrontRightKpa { get; init; }
    public double? TirePressureRearLeftKpa { get; init; }
    public double? TirePressureRearRightKpa { get; init; }
    public double? TireTemperatureFrontLeftC { get; init; }
    public double? TireTemperatureFrontRightC { get; init; }
    public double? TireTemperatureRearLeftC { get; init; }
    public double? TireTemperatureRearRightC { get; init; }
    public double? OdometerKm { get; init; }
    public double? InteriorTemperatureC { get; init; }
    public string? ChargingStatus { get; init; }
    public bool? ChargingActive { get; init; }
    public bool? ChargePlugConnected { get; init; }
    public bool? AcActive { get; init; }
    public bool? Locked { get; init; }
    public bool? WindowFrontLeftOpen { get; init; }
    public bool? WindowFrontRightOpen { get; init; }
    public bool? WindowRearLeftOpen { get; init; }
    public bool? WindowRearRightOpen { get; init; }
    public bool? WindowFrontDriverOpen { get; init; }
    public bool? WindowFrontPassengerOpen { get; init; }
    public bool? WindowRearDriverSideOpen { get; init; }
    public bool? WindowRearPassengerSideOpen { get; init; }
    public bool? DoorFrontDriverOpen { get; init; }
    public bool? DoorFrontPassengerOpen { get; init; }
    public bool? DoorRearDriverSideOpen { get; init; }
    public bool? DoorRearPassengerSideOpen { get; init; }
    public bool? TrunkOpen { get; init; }
    public int? SunroofPositionCode { get; init; }
    public bool? AirCirculation { get; init; }
    public bool? FrontDefroster { get; init; }
    public bool? RearDefroster { get; init; }
    public bool? GpsAuthorized { get; init; }
    public int? TirePressureStateFrontLeft { get; init; }
    public int? TirePressureStateFrontRight { get; init; }
    public int? TirePressureStateRearLeft { get; init; }
    public int? TirePressureStateRearRight { get; init; }
    public int? TireTemperatureStateFrontLeft { get; init; }
    public int? TireTemperatureStateFrontRight { get; init; }
    public int? TireTemperatureStateRearLeft { get; init; }
    public int? TireTemperatureStateRearRight { get; init; }
    public int? WindowLearnFrontLeft { get; init; }
    public int? WindowLearnFrontRight { get; init; }
    public int? WindowLearnRearLeft { get; init; }
    public int? WindowLearnRearRight { get; init; }
    public bool? SteeringWheelHeaterActive { get; init; }
    public int? RearLeftSeatHeaterLevel { get; init; }
    public int? RearRightSeatHeaterLevel { get; init; }
    public bool? FrontWindscreenHeaterActive { get; init; }
    public int? EngineStateCode { get; init; }
    public int? FrontDriverSeatHeaterLevel { get; init; }
    public int? FrontPassengerSeatHeaterLevel { get; init; }
    public int? FrontDriverSeatVentLevel { get; init; }
    public int? FrontPassengerSeatVentLevel { get; init; }
    public bool? NearBeamActive { get; init; }
    public bool? FarBeamActive { get; init; }
    public bool? LeftTurnLampActive { get; init; }
    public bool? RightTurnLampActive { get; init; }
    public bool? OilAlarmActive { get; init; }
    public bool? EngineDoorOpen { get; init; }
    public bool? AcAutoModeActive { get; init; }
    public bool? AirCleanActive { get; init; }
    public bool? CabinCleanActive { get; init; }
    public bool? BackDoorOpen { get; init; }
    public double? ChargeSoc { get; init; }
    public int? ChargingGunModel { get; init; }
    public int? HcuPowertrainState { get; init; }
    public double? Power { get; init; }
    public int? BatteryPackState { get; init; }
    public int? AccCleanOff { get; init; }
    public int? TboxState { get; init; }
    public int? WirelessLevel { get; init; }
    public int? OilSegments { get; init; }
    public bool? TirePressureIndicatorFrontLeft { get; init; }
    public bool? TirePressureIndicatorFrontRight { get; init; }
    public bool? TirePressureIndicatorRearLeft { get; init; }
    public bool? TirePressureIndicatorRearRight { get; init; }
    public double? AuxBatteryLevel { get; init; }
    public double? RemainingUsableChargePercent { get; init; }
    public double? BatteryPackCurrent { get; init; }
    public double? BatteryPackVoltage { get; init; }
    public bool? InsertGunKeepWarm { get; init; }
    public bool? ActiveKeepWarm { get; init; }
    public string? LatestRemoteRecordMsg { get; init; }
}

public sealed class ClimateSnapshot
{
    public string Mode { get; init; } = "off";
    public string Action { get; init; } = "off";
    public int TargetTemperatureC { get; init; } = 22;
    public int OperationTimeMinutes { get; init; } = 15;
    public double? CurrentTemperatureC { get; init; }
    public int MinTemperatureC { get; init; } = 16;
    public int MaxTemperatureC { get; init; } = 32;
    public int StepTemperatureC { get; init; } = 1;
}

public sealed class RawItemSnapshot
{
    public string? Value { get; init; }
    public string? Unit { get; init; }
}

public sealed class ClimateCommandRequest
{
    public string? Mode { get; init; }
    public int? Temperature { get; init; }
    public int? OperationTimeMinutes { get; init; }
}

public sealed class LockCommandRequest
{
    public string Action { get; init; } = String.Empty;
}

public sealed class VehicleControlCommandRequest
{
    public string Action { get; init; } = String.Empty;
    public int? RunTimeMinutes { get; init; }
}

// 智能预约充电（beantech）：车端只有一个 chargingMode 开关 + 一个 customTime 时间窗。
public sealed class ChargingModeState
{
    public bool Enabled { get; init; }
    public string? StartTime { get; init; }
    public string? EndTime { get; init; }
}

public sealed class ChargingModeRequest
{
    public bool? Enable { get; init; }
}

// Charging schedule (vehicleCharge/setChargingPlan). Times are epoch milliseconds.
// Enable + a [start,end] window makes the car charge only within it; disable (enable=false)
// clears the plan (car charges on plug-in). No security PIN required.
public sealed class ChargingPlanRequest
{
    public bool? Enable { get; init; }
    public long? StartTime { get; init; }
    public long? EndTime { get; init; }
    public int? PlanType { get; init; }
    public string? Weeks { get; init; }
}
