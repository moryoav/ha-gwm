using System.Globalization;
using GwmOra.Addon.Models;
using libgwmapi.DTO.Vehicle;

namespace GwmOra.Addon.Gwm;

public static class VehicleSnapshotMapper
{
    public const int MinimumOperationTimeMinutes = 5;
    public const int MaximumOperationTimeMinutes = 30;
    public const int OperationTimeStepMinutes = 1;
    public const int DefaultOperationTimeMinutes = 15;

    public static VehicleSnapshot Map(
        Vehicle vehicle,
        VehicleStatus status,
        VehicleBasicsInfo basics,
        bool remoteCommandsAvailable,
        string commandStatus)
    {
        var values = new VehicleValues
        {
            Soc = Number(status, "2013021"),
            RangeKm = Number(status, "2011501"),
            FuelLevelL = Number(status, "2017002"),
            FuelRangeKm = Number(status, "2011007"),
            RemainingChargingTimeMin = Number(status, "2013022"),
            Soce = Number(status, "2041301"),
            TirePressureFrontLeftKpa = Number(status, "2101001"),
            TirePressureFrontRightKpa = Number(status, "2101002"),
            TirePressureRearLeftKpa = Number(status, "2101003"),
            TirePressureRearRightKpa = Number(status, "2101004"),
            TireTemperatureFrontLeftC = Number(status, "2101005"),
            TireTemperatureFrontRightC = Number(status, "2101006"),
            TireTemperatureRearLeftC = Number(status, "2101007"),
            TireTemperatureRearRightC = Number(status, "2101008"),
            OdometerKm = Number(status, "2103010"),
            InteriorTemperatureC = Number(status, "2201001") / 10.0,
            ChargingActive = Bool(status, "2041142"),
            ChargePlugConnected = Bool(status, "2042082"),
            AcActive = Bool(status, "2202001"),
            Locked = LockClosed(status),
            WindowFrontLeftOpen = WindowOpen(status, "2210001"),
            WindowFrontRightOpen = WindowOpen(status, "2210002"),
            WindowRearLeftOpen = WindowOpen(status, "2210003"),
            WindowRearRightOpen = WindowOpen(status, "2210004"),
            DoorFrontLeftOpen = Bool(status, "2206004"),
            DoorFrontRightOpen = Bool(status, "2206002"),
            DoorRearLeftOpen = Bool(status, "2206005"),
            DoorRearRightOpen = Bool(status, "2206003"),
            TrunkOpen = Bool(status, "2206001"),
            Roof = Number(status, "2210005"),
            AirCirculation = Bool(status, "2078020"),
            FrontDefroster = Bool(status, "2222001"),
            RearDefroster = Bool(status, "2210032"),
            GpsAuthorized = Bool(status, "2310001"),
            TirePressureStateFrontLeft = Number(status, "2102001"),
            TirePressureStateFrontRight = Number(status, "2102002"),
            TirePressureStateRearLeft = Number(status, "2102003"),
            TirePressureStateRearRight = Number(status, "2102004"),
            TireTemperatureStateFrontLeft = Number(status, "2102007"),
            TireTemperatureStateFrontRight = Number(status, "2102008"),
            TireTemperatureStateRearLeft = Number(status, "2102009"),
            TireTemperatureStateRearRight = Number(status, "2102010"),
            // Community Haval table: 2210011 FL, 2210010 FR, 2210013 RL, 2210012 RR.
            WindowLearnFrontLeft = Number(status, "2210011"),
            WindowLearnFrontRight = Number(status, "2210010"),
            WindowLearnRearLeft = Number(status, "2210013"),
            WindowLearnRearRight = Number(status, "2210012"),
            SteeringWheelHeater = Number(status, "2060016"),
            RearLeftSeatHeaterLevel = Number(status, "2424001"),
            RearRightSeatHeaterLevel = Number(status, "2424002"),
            FrontWindscreenHeater = Number(status, "2202111"),
            EngineState = Number(status, "2016001"),
            FrontDriverSeatHeaterLevel = Number(status, "2220001"),
            FrontPassengerSeatHeaterLevel = Number(status, "2220002")
        };

        var acOn = values.AcActive == true;
        var targetTemperature = NormalizeTemperature(basics.Config?.AirConditionerTemperature, 22);
        var operationTimeMinutes = NormalizeOperationTime(
            basics.Config?.AirConditionerStatusTime,
            DefaultOperationTimeMinutes);

        return new VehicleSnapshot
        {
            Vin = vehicle.Vin,
            Name = FirstNonEmpty(vehicle.AppShowSeriesName, vehicle.VehicleNick?.ToString(), vehicle.ModelName, "GWM ORA"),
            Manufacturer = FirstNonEmpty(vehicle.BrandName, vehicle.OtBrandName, "GWM"),
            Model = FirstNonEmpty(vehicle.Vtype, vehicle.VTypeName, vehicle.ModelName),
            SerialNumber = status.DeviceId,
            Location = status.Latitude.HasValue && status.Longitude.HasValue
                ? new LocationSnapshot { Latitude = status.Latitude.Value, Longitude = status.Longitude.Value }
                : null,
            Timestamps = new TimestampSnapshot
            {
                AcquisitionTime = UnixMilliseconds(status.AcquisitionTime),
                UpdateTime = UnixMilliseconds(status.UpdateTime),
                LastRefresh = DateTimeOffset.UtcNow
            },
            Capabilities = new VehicleCapabilities
            {
                RemoteCommands = remoteCommandsAvailable
            },
            Values = values,
            Climate = new ClimateSnapshot
            {
                Mode = acOn ? "cool" : "off",
                Action = acOn ? "cooling" : "off",
                TargetTemperatureC = targetTemperature,
                OperationTimeMinutes = operationTimeMinutes,
                CurrentTemperatureC = values.InteriorTemperatureC
            },
            CommandStatus = commandStatus,
            RawItems = RawItems(status)
        };
    }

    public static int NormalizeTemperature(string? value, int fallback)
    {
        return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 16, 32)
            : fallback;
    }

    public static bool TryGetValidTemperature(string? value, out int temperature)
    {
        if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed is >= 16 and <= 32)
        {
            temperature = parsed;
            return true;
        }

        temperature = default;
        return false;
    }

    public static int NormalizeOperationTime(string? value, int fallback)
    {
        if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var storedValue))
        {
            return fallback;
        }

        // Releases before 0.4.0 wrote minutes directly into this seconds-based field.
        if (IsValidOperationTime(storedValue))
        {
            return storedValue;
        }

        if (storedValue % 60 != 0)
        {
            return fallback;
        }

        var minutes = storedValue / 60;
        return IsValidOperationTime(minutes) ? minutes : fallback;
    }

    public static bool IsValidOperationTime(int minutes)
    {
        return minutes is >= MinimumOperationTimeMinutes and <= MaximumOperationTimeMinutes
               && minutes % OperationTimeStepMinutes == 0;
    }

    private static double? Number(VehicleStatus status, string code)
    {
        var value = Value(status, code);
        return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool? Bool(VehicleStatus status, string code)
    {
        return Value(status, code) switch
        {
            "1" => true,
            "0" => false,
            null => null,
            _ => null
        };
    }

    private static bool? LockClosed(VehicleStatus status)
    {
        return Value(status, "2208001") switch
        {
            "0" => true,
            "1" => false,
            null => null,
            _ => null
        };
    }

    private static bool? WindowOpen(VehicleStatus status, string code)
    {
        var value = Value(status, code);
        return value is null ? null : value != "1";
    }

    private static string? Value(VehicleStatus status, string code)
    {
        return status.Items?.FirstOrDefault(x => code.Equals(x.Code, StringComparison.Ordinal))?.Value?.ToString();
    }

    private static DateTimeOffset? UnixMilliseconds(long value)
    {
        return value > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(value) : null;
    }

    private static IReadOnlyDictionary<string, RawItemSnapshot> RawItems(VehicleStatus status)
    {
        return (status.Items ?? Array.Empty<VehicleStatusItems>())
            .Where(x => x.Value is not null)
            .ToDictionary(
                x => x.Code,
                x => new RawItemSnapshot { Value = x.Value?.ToString(), Unit = x.Unit },
                StringComparer.Ordinal);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !String.IsNullOrWhiteSpace(value)) ?? String.Empty;
    }
}
