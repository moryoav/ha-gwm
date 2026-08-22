using System.Globalization;
using System.Text.Json;
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
        var rawItems = RawItems(status);
        var values = new VehicleValues
        {
            Soc = Number(rawItems, "2013021"),
            RangeKm = Number(rawItems, "2011501"),
            FuelLevelL = NonNegativeNumber(rawItems, "2017002"),
            FuelRangeKm = NonNegativeNumber(rawItems, "2011007"),
            RemainingChargingTimeMin = Number(rawItems, "2013022"),
            Soce = Number(rawItems, "2041301"),
            TirePressureFrontLeftKpa = Number(rawItems, "2101001"),
            TirePressureFrontRightKpa = Number(rawItems, "2101002"),
            TirePressureRearLeftKpa = Number(rawItems, "2101003"),
            TirePressureRearRightKpa = Number(rawItems, "2101004"),
            TireTemperatureFrontLeftC = Number(rawItems, "2101005"),
            TireTemperatureFrontRightC = Number(rawItems, "2101006"),
            TireTemperatureRearLeftC = Number(rawItems, "2101007"),
            TireTemperatureRearRightC = Number(rawItems, "2101008"),
            OdometerKm = Number(rawItems, "2103010"),
            InteriorTemperatureC = Number(rawItems, "2201001") / 10.0,
            ChargingStatus = ChargingStatus(rawItems),
            ChargingActive = ChargingActive(rawItems),
            ChargePlugConnected = Bool(rawItems, "2042082"),
            AcActive = Bool(rawItems, "2202001"),
            Locked = LockClosed(rawItems),
            // Keep the released left/right fields as compatibility aliases. The canonical
            // fields below follow the app's market-aware driver/passenger mapping.
            WindowFrontLeftOpen = WindowOpen(rawItems, "2210001"),
            WindowFrontRightOpen = WindowOpen(rawItems, "2210002"),
            WindowRearLeftOpen = WindowOpen(rawItems, "2210003"),
            WindowRearRightOpen = WindowOpen(rawItems, "2210004"),
            WindowFrontDriverOpen = WindowOpen(rawItems, "2210001"),
            WindowFrontPassengerOpen = WindowOpen(rawItems, "2210002"),
            WindowRearDriverSideOpen = WindowOpen(rawItems, "2210004"),
            WindowRearPassengerSideOpen = WindowOpen(rawItems, "2210003"),
            DoorFrontDriverOpen = Bool(rawItems, "2206002"),
            DoorFrontPassengerOpen = Bool(rawItems, "2206004"),
            DoorRearDriverSideOpen = Bool(rawItems, "2206003"),
            DoorRearPassengerSideOpen = Bool(rawItems, "2206005"),
            TrunkOpen = Bool(rawItems, "2206001"),
            SunroofPositionCode = Integer(rawItems, "2210005"),
            AirCirculation = Bool(rawItems, "2078020"),
            FrontDefroster = Bool(rawItems, "2222001"),
            RearDefroster = Bool(rawItems, "2210032"),
            GpsAuthorized = Bool(rawItems, "2310001"),
            TirePressureStateFrontLeft = Integer(rawItems, "2102001"),
            TirePressureStateFrontRight = Integer(rawItems, "2102002"),
            TirePressureStateRearLeft = Integer(rawItems, "2102003"),
            TirePressureStateRearRight = Integer(rawItems, "2102004"),
            TireTemperatureStateFrontLeft = Integer(rawItems, "2102007"),
            TireTemperatureStateFrontRight = Integer(rawItems, "2102008"),
            TireTemperatureStateRearLeft = Integer(rawItems, "2102009"),
            TireTemperatureStateRearRight = Integer(rawItems, "2102010"),
            // Community Haval table: 2210011 FL, 2210010 FR, 2210013 RL, 2210012 RR.
            WindowLearnFrontLeft = Integer(rawItems, "2210011"),
            WindowLearnFrontRight = Integer(rawItems, "2210010"),
            WindowLearnRearLeft = Integer(rawItems, "2210013"),
            WindowLearnRearRight = Integer(rawItems, "2210012"),
            SteeringWheelHeaterActive = Bool(rawItems, "2060016"),
            RearLeftSeatHeaterLevel = Level(rawItems, "2424001"),
            RearRightSeatHeaterLevel = Level(rawItems, "2424002"),
            FrontWindscreenHeaterActive = Bool(rawItems, "2202111"),
            EngineStateCode = Integer(rawItems, "2016001"),
            FrontDriverSeatHeaterLevel = Level(rawItems, "2220001"),
            FrontPassengerSeatHeaterLevel = Level(rawItems, "2220002"),
            FrontDriverSeatVentLevel = Level(rawItems, "2220003"),
            FrontPassengerSeatVentLevel = Level(rawItems, "2220004")
        };

        var acOn = values.AcActive == true;
        var targetTemperature = NormalizeTemperature(basics.Config?.AirConditionerTemperature, 22);
        var operationTimeMinutes = NormalizeOperationTime(
            basics.Config?.AirConditionerStatusTime,
            DefaultOperationTimeMinutes);

        return new VehicleSnapshot
        {
            Vin = vehicle.Vin,
            Name = FirstNonEmpty(vehicle.AppShowSeriesName, vehicle.VehicleNick?.ToString(), vehicle.ModelName, "GWM vehicle"),
            Manufacturer = FirstNonEmpty(vehicle.BrandName, vehicle.OtBrandName, "GWM"),
            Model = FirstNonEmpty(vehicle.Vtype, vehicle.VTypeName, vehicle.ModelName),
            SerialNumber = status.DeviceId,
            Location = Location(status.Latitude, status.Longitude),
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
            RawItems = rawItems
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

    private static double? Number(IReadOnlyDictionary<string, RawItemSnapshot> items, string code)
    {
        var value = Value(items, code);
        return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
               && Double.IsFinite(parsed)
            ? parsed
            : null;
    }

    private static double? NonNegativeNumber(IReadOnlyDictionary<string, RawItemSnapshot> items, string code)
    {
        var value = Number(items, code);
        return value is >= 0 ? value : null;
    }

    private static int? Integer(IReadOnlyDictionary<string, RawItemSnapshot> items, string code)
    {
        var value = Number(items, code);
        if (!value.HasValue || value.Value != Math.Truncate(value.Value)
                            || value.Value < Int32.MinValue || value.Value > Int32.MaxValue)
        {
            return null;
        }

        return (int)value.Value;
    }

    private static int? Level(IReadOnlyDictionary<string, RawItemSnapshot> items, string code)
    {
        var value = Integer(items, code);
        return value is >= 0 and <= 3 ? value : null;
    }

    private static bool? Bool(IReadOnlyDictionary<string, RawItemSnapshot> items, string code)
    {
        return Integer(items, code) switch
        {
            1 => true,
            0 => false,
            _ => null
        };
    }

    private static string? ChargingStatus(IReadOnlyDictionary<string, RawItemSnapshot> items)
    {
        return Integer(items, "2041142") switch
        {
            0 when Bool(items, "2042082") == false => "disconnected",
            0 when Bool(items, "2042082") == true => "connected",
            1 => "charging",
            2 => "awaiting_charging",
            5 => "waiting_for_power",
            6 => "error",
            _ => null
        };
    }

    private static bool? ChargingActive(IReadOnlyDictionary<string, RawItemSnapshot> items)
    {
        return Integer(items, "2041142") switch
        {
            1 => true,
            0 or 2 or 5 or 6 => false,
            _ => null
        };
    }

    private static bool? LockClosed(IReadOnlyDictionary<string, RawItemSnapshot> items)
    {
        return Integer(items, "2208001") switch
        {
            0 => true,
            1 => false,
            _ => null
        };
    }

    private static bool? WindowOpen(IReadOnlyDictionary<string, RawItemSnapshot> items, string code)
    {
        return Integer(items, code) switch
        {
            1 => false,
            >= 0 => true,
            _ => null
        };
    }

    private static string? Value(IReadOnlyDictionary<string, RawItemSnapshot> items, string code)
    {
        return items.TryGetValue(code, out var item) ? item.Value?.Trim() : null;
    }

    private static DateTimeOffset? UnixMilliseconds(long value)
    {
        if (value <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static LocationSnapshot? Location(double? latitude, double? longitude)
    {
        if (!latitude.HasValue || !longitude.HasValue
                               || !Double.IsFinite(latitude.Value) || !Double.IsFinite(longitude.Value)
                               || latitude.Value is < -90 or > 90
                               || longitude.Value is < -180 or > 180)
        {
            return null;
        }

        return new LocationSnapshot
        {
            Latitude = latitude.Value,
            Longitude = longitude.Value
        };
    }

    private static IReadOnlyDictionary<string, RawItemSnapshot> RawItems(VehicleStatus status)
    {
        var items = new Dictionary<string, RawItemSnapshot>(StringComparer.Ordinal);
        foreach (var item in status.Items ?? Array.Empty<VehicleStatusItems>())
        {
            if (item is null || String.IsNullOrWhiteSpace(item.Code))
            {
                continue;
            }

            var value = NormalizeValue(item.Value);
            if (value is null)
            {
                continue;
            }

            items[item.Code.Trim()] = new RawItemSnapshot
            {
                Value = value,
                Unit = item.Unit
            };
        }

        return items;
    }

    private static string? NormalizeValue(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            JsonElement json => json.GetRawText(),
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !String.IsNullOrWhiteSpace(value)) ?? String.Empty;
    }
}
