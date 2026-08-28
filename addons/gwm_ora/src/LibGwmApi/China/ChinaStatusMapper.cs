#nullable enable

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using libgwmapi.DTO.Vehicle;

namespace libgwmapi.China;

/// <summary>
/// Converts the field-oriented AutoAI status used by the mainland-China app to the
/// signal-code representation already consumed by the Home Assistant add-on.
/// </summary>
internal static class ChinaStatusMapper
{
    internal static VehicleStatus Map(JsonNode responseBody, Vehicle vehicle)
    {
        var vehicleStatus = Property(responseBody, "vehicleSts") ?? responseBody;
        var car = Property(vehicleStatus, "carStatus");
        var battery = Property(vehicleStatus, "battSts");
        var lastUpdate = Long(Property(vehicleStatus, "lastUpdate"))
                         ?? Long(Property(car, "uploadTime"))
                         ?? 0;
        var items = new List<VehicleStatusItems>();

        // China status payloads vary by vehicle. Prefer the dedicated battery
        // field, but VV6/NavInfo payloads expose the app's battery level as
        // carStatus.soc instead.
        Add(items, "2013021", FirstValue(battery, "battSoc", car, "soc"), "%");
        var remainingRange = FirstValue(battery, "hcuEVContnsDistance", car, "hcuEvcontnsdistance");
        Add(items, "2011501", remainingRange, "km");
        Add(items, "2013022", NonNegativeValue(battery, "chgTime"), "min");
        Add(items, "2041301", Value(battery, "battSoh"), "%");
        if (AddFuelLevel(items, vehicle, car))
        {
            Add(items, "2011007", remainingRange, "km");
        }

        AddTire(items, car, "drv", "2101001", "2101005", "2102001", "2102007");
        AddTire(items, car, "pass", "2101002", "2101006", "2102002", "2102008");
        AddTire(items, car, "rl", "2101003", "2101007", "2102003", "2102009");
        AddTire(items, car, "rr", "2101004", "2101008", "2102004", "2102010");

        Add(items, "2103010", Value(car, "vehTotDistance"), "km");
        Add(items, "2041142", ChargeStatusValue(battery));
        Add(items, "2042082", ChargePlugCode(battery));
        Add(items, "2202001", AirConditioningCode(car));
        Add(items, "2208001", LockCode(vehicle, car));

        Add(items, "2210001", WindowCode(car, "drvWinPosnSts"));
        Add(items, "2210002", WindowCode(car, "passWinPosnSts"));
        Add(items, "2210003", WindowCode(car, "rlWinPosnSts"));
        Add(items, "2210004", WindowCode(car, "rrWinPosnSts"));
        Add(items, "2210005", Value(car, "srPosnSts"));
        Add(items, "2210011", Value(car, "drvWinLrnSts"));
        Add(items, "2210010", Value(car, "passWinLrnSts"));
        Add(items, "2210013", Value(car, "rlWinLrnSts"));
        Add(items, "2210012", Value(car, "rrWinLrnSts"));

        Add(items, "2206002", OpenCode(car, "drvDoorSts"));
        Add(items, "2206004", OpenCode(car, "passDoorSts"));
        Add(items, "2206003", OpenCode(car, "rlDoorSts"));
        Add(items, "2206005", OpenCode(car, "rrDoorSts"));
        Add(items, "2206001", OpenCode(car, "trunkSts"));
        Add(items, "2210032", EnabledWhenValid(car, "achtdrrwndValid", "rearDefrostState"));
        Add(items, "2060016", BinaryValue(car, "steerwheelheatdsts"));
        Add(items, "2016001", EngineCode(car));
        Add(items, "2220001", ComfortLevel(car, "driverseatheatstsValid", "seatHeatingMainState", "1"));
        Add(items, "2220002", ComfortLevel(car, "passseatheatstsValid", "seatHeatingDeputyState", "1"));
        Add(items, "2220003", ComfortLevel(car, "driverseatventstsValid", "seatHeatingMainState", "2"));
        Add(items, "2220004", ComfortLevel(car, "passseatventstsValid", "seatHeatingDeputyState", "2"));

        var latitude = Double(Property(car, "lat"));
        var longitude = Double(Property(car, "lon"));
        if (latitude.HasValue && longitude.HasValue)
        {
            Add(items, "2310001", "1");
        }

        return new VehicleStatus
        {
            AcquisitionTime = lastUpdate,
            UpdateTime = lastUpdate,
            UploadTime = Long(Property(car, "uploadTime")),
            DeviceId = FirstNonEmpty(vehicle.VehicleId, vehicle.Vin),
            Latitude = latitude,
            Longitude = longitude,
            Items = items.ToArray(),
            GlobalStatusList = Array.Empty<object>()
        };
    }

    internal static ChargingInfos ChargingInfo(JsonNode responseBody, string vin)
    {
        var vehicleStatus = Property(responseBody, "vehicleSts") ?? responseBody;
        var settings = Property(vehicleStatus, "chargeSettings");
        var mode = Value(settings, "mode");
        if (settings is null || String.IsNullOrWhiteSpace(mode) || mode == "1")
        {
            return new ChargingInfos();
        }

        var startText = FirstNonEmpty(
            Value(settings, "phoneStrtHourMin"),
            Value(settings, "discountStartTime"));
        var endText = FirstNonEmpty(
            Value(settings, "phoneEndHourMin"),
            Value(settings, "discountEndTime"));
        if (!TimeOnly.TryParseExact(startText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(endText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            return new ChargingInfos();
        }

        var chinaNow = ChinaTime.Convert(DateTimeOffset.UtcNow);
        var date = DateOnly.FromDateTime(chinaNow.DateTime);
        var startLocal = date.ToDateTime(start, DateTimeKind.Unspecified);
        var endLocal = date.ToDateTime(end, DateTimeKind.Unspecified);
        if (endLocal <= startLocal)
        {
            endLocal = endLocal.AddDays(1);
        }

        var repeat = Value(settings, "repeatTimes");
        if (String.IsNullOrWhiteSpace(repeat))
        {
            repeat = String.Concat(
                DayEnabled(settings, "sundayUseTime"),
                DayEnabled(settings, "saturdayUseTime"),
                DayEnabled(settings, "fridayUseTime"),
                DayEnabled(settings, "thurdayUseTime"),
                DayEnabled(settings, "wednesdayUseTime"),
                DayEnabled(settings, "tuesdayUseTime"),
                DayEnabled(settings, "mondayUseTime"));
        }

        return new ChargingInfos
        {
            ChargePlanList = new[]
            {
                new ChargePlanItem
                {
                    PlanId = StablePlanId(vin),
                    PlanType = "0",
                    StartTime = new DateTimeOffset(startLocal, ChinaTime.UtcOffset).ToUnixTimeMilliseconds(),
                    EndTime = new DateTimeOffset(endLocal, ChinaTime.UtcOffset).ToUnixTimeMilliseconds(),
                    Weeks = repeat ?? String.Empty
                }
            }
        };
    }

    private static bool AddFuelLevel(List<VehicleStatusItems> items, Vehicle vehicle, JsonNode? car)
    {
        var direct = ValidFuelLevel(car, vehicle);
        if (direct is not null)
        {
            Add(items, "2017002", direct, "L");
            return true;
        }

        var segments = Double(Property(car, "oilQty"));
        var tankCapacity = ObjectNumber(vehicle.TankCapacity);
        if (segments is >= 0 and <= 8 && tankCapacity is > 0)
        {
            Add(items, "2017002", (segments.Value * tankCapacity.Value / 8.0)
                .ToString("0.###", CultureInfo.InvariantCulture), "L");
            return true;
        }

        return false;
    }

    private static void AddTire(
        List<VehicleStatusItems> items,
        JsonNode? car,
        string prefix,
        string pressureCode,
        string temperatureCode,
        string pressureStateCode,
        string temperatureStateCode)
    {
        var pressure = Value(car, prefix + "TirePress");
        if (pressure is not ("349" or "350"))
        {
            Add(items, pressureCode, pressure, "kPa");
        }

        var temperature = Value(car, prefix + "TireTemp");
        if (temperature != "-50")
        {
            Add(items, temperatureCode, temperature, "°C");
        }

        Add(items, pressureStateCode, Value(car, prefix + "TirePressState"));
        Add(items, temperatureStateCode, Value(car, prefix + "TireTempState"));
    }

    private static string? ChargePlugCode(JsonNode? battery)
    {
        var dc = Integer(Property(battery, "bmsDCChrgConnect"));
        var obc = Integer(Property(battery, "obcSts"));
        if (!dc.HasValue && !obc.HasValue)
        {
            return null;
        }

        return dc is 1 or 2 || obc == 1 ? "1" : "0";
    }

    private static string? ChargeStatusValue(JsonNode? battery)
    {
        var dc = Integer(Property(battery, "bmsDCChrgConnect"));
        if (dc is 1 or 2)
        {
            return Integer(Property(battery, "bmsChrgsts")) switch
            {
                2 => "3",
                3 => "6",
                int value => value.ToString(CultureInfo.InvariantCulture),
                _ => null
            };
        }

        return Value(battery, "chgSts");
    }

    private static string? ValidFuelLevel(JsonNode? car, Vehicle vehicle)
    {
        var validity = Integer(Property(car, "remainFuelSts"));
        var value = Double(Property(car, "remainFuel"));
        if (validity != 1 || value is null or < 0)
        {
            return null;
        }

        var tankCapacity = ObjectNumber(vehicle.TankCapacity);
        if (tankCapacity is > 0 && value > tankCapacity)
        {
            return null;
        }

        return value.Value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string? AirConditioningCode(JsonNode? car)
    {
        var valid = Value(car, "cdngoffValid");
        var state = Value(car, "cdngoff");
        if (valid is null && state is null)
        {
            return null;
        }

        return valid == "1" && state == "0" ? "1" : "0";
    }

    private static string? LockCode(Vehicle vehicle, JsonNode? car)
    {
        var raw = Integer(Property(car, "drvDoorLockSts"));
        if (!raw.HasValue)
        {
            return null;
        }

        // The verified VV6/NavInfo payload uses 0 for locked. Other network-type-2
        // variants observed in the app protocol can use 2 or 3 for the same state.
        var locked = vehicle.VehicleNetworkType == 2 ? raw is 0 or 2 or 3 : raw == 1;
        return locked ? "0" : "1";
    }

    private static string? WindowCode(JsonNode? car, string property)
    {
        var value = Integer(Property(car, property));
        return value.HasValue ? value == 1 ? "0" : "1" : null;
    }

    private static string? OpenCode(JsonNode? car, string property)
    {
        var value = Integer(Property(car, property));
        return value.HasValue ? value == 1 ? "1" : "0" : null;
    }

    private static string? EnabledWhenValid(JsonNode? car, string validProperty, string stateProperty) =>
        Value(car, validProperty) == "1" ? BinaryValue(car, stateProperty) : null;

    private static string? EngineCode(JsonNode? car)
    {
        if (Value(car, "engstsValid") != "1")
        {
            return null;
        }

        return Value(car, "engSts") == "1" ? "1" : "0";
    }

    private static string? ComfortLevel(
        JsonNode? car,
        string validProperty,
        string stateProperty,
        string activeValue) =>
        Value(car, validProperty) == "1"
            ? Value(car, stateProperty) == activeValue ? "1" : "0"
            : null;

    private static string? BinaryValue(JsonNode? node, string property)
    {
        var value = Value(node, property);
        return value is "0" or "1" ? value : null;
    }

    private static string? NonNegativeValue(JsonNode? node, string property)
    {
        var value = Double(Property(node, property));
        return value is >= 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static string? FirstValue(JsonNode? first, string firstProperty, JsonNode? second, string secondProperty) =>
        FirstNonEmpty(Value(first, firstProperty), Value(second, secondProperty));

    private static void Add(List<VehicleStatusItems> items, string code, string? value, string? unit = null)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return;
        }

        items.Add(new VehicleStatusItems { Code = code, Value = value, Unit = unit });
    }

    private static JsonNode? Property(JsonNode? node, string name)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        if (obj.TryGetPropertyValue(name, out var direct))
        {
            return direct;
        }

        return obj.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static string? Value(JsonNode? node, string property) => Scalar(Property(node, property));

    private static string? Scalar(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return text;
            }
            if (value.TryGetValue<long>(out var integer))
            {
                return integer.ToString(CultureInfo.InvariantCulture);
            }
            if (value.TryGetValue<double>(out var number))
            {
                return number.ToString(CultureInfo.InvariantCulture);
            }
            if (value.TryGetValue<bool>(out var boolean))
            {
                return boolean ? "1" : "0";
            }
        }

        return null;
    }

    private static int? Integer(JsonNode? node) =>
        Int32.TryParse(Scalar(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static long? Long(JsonNode? node) =>
        Int64.TryParse(Scalar(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static double? Double(JsonNode? node) =>
        System.Double.TryParse(Scalar(node), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && System.Double.IsFinite(value)
            ? value
            : null;

    private static double? ObjectNumber(object? value)
    {
        try
        {
            return value switch
            {
                null => null,
                JsonElement element when element.ValueKind == JsonValueKind.Number
                                          && element.TryGetDouble(out var number) => number,
                JsonElement element when element.ValueKind == JsonValueKind.String =>
                    System.Double.TryParse(
                        element.GetString(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var number)
                        ? number
                        : null,
                IConvertible convertible => convertible.ToDouble(CultureInfo.InvariantCulture),
                _ => null
            };
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static string DayEnabled(JsonNode? settings, string property)
    {
        var value = Value(settings, property);
        return String.IsNullOrWhiteSpace(value) || value == "0" ? "0" : "1";
    }

    private static long StablePlanId(string vin)
    {
        var hash = ChinaCrypto.Sha256Hex(vin ?? String.Empty);
        return Int64.Parse(hash[..15], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !String.IsNullOrWhiteSpace(value)) ?? String.Empty;
}
