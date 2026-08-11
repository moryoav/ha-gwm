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
    public int PollIntervalSeconds { get; init; }
    public DateTimeOffset? LastRefresh { get; init; }
    public string? LastError { get; init; }
}

public sealed class VehiclesResponse
{
    public int ApiVersion { get; init; } = 1;
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool RemoteCommandsEnabled { get; init; }
    public bool SecurityPinConfigured { get; init; }
    public IReadOnlyList<VehicleSnapshot> Vehicles { get; init; } = Array.Empty<VehicleSnapshot>();
}

public sealed class VehicleSnapshot
{
    public string Vin { get; init; } = String.Empty;
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
    public bool? DoorFrontLeftOpen { get; init; }
    public bool? DoorFrontRightOpen { get; init; }
    public bool? DoorRearLeftOpen { get; init; }
    public bool? DoorRearRightOpen { get; init; }
    public bool? TrunkOpen { get; init; }
    public double? Roof { get; init; }
    public bool? AirCirculation { get; init; }
    public bool? FrontDefroster { get; init; }
    public bool? RearDefroster { get; init; }
    public bool? GpsAuthorized { get; init; }
    public double? TirePressureStateFrontLeft { get; init; }
    public double? TirePressureStateFrontRight { get; init; }
    public double? TirePressureStateRearLeft { get; init; }
    public double? TirePressureStateRearRight { get; init; }
    public double? TireTemperatureStateFrontLeft { get; init; }
    public double? TireTemperatureStateFrontRight { get; init; }
    public double? TireTemperatureStateRearLeft { get; init; }
    public double? TireTemperatureStateRearRight { get; init; }
    public double? WindowLearnFrontLeft { get; init; }
    public double? WindowLearnFrontRight { get; init; }
    public double? WindowLearnRearLeft { get; init; }
    public double? WindowLearnRearRight { get; init; }
    public double? SteeringWheelHeater { get; init; }
    public double? RearLeftSeatHeaterLevel { get; init; }
    public double? RearRightSeatHeaterLevel { get; init; }
    public double? FrontWindscreenHeater { get; init; }
    public double? EngineState { get; init; }
    public double? FrontDriverSeatHeaterLevel { get; init; }
    public double? FrontPassengerSeatHeaterLevel { get; init; }
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
