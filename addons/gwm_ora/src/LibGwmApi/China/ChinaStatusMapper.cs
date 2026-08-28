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

    internal static VehicleStatus MapBeanTech(JsonNode responseBody, Vehicle vehicle)
    {
        var data = Property(responseBody, "data") ?? responseBody;
        var status = Property(data, "vehicleStatusInfo") ?? data;
        var door = Property(status, "door");
        var tirePress = Property(status, "tirePress");
        var tireTemp = Property(status, "tireTemp");
        var seat = Property(status, "seat");
        var windows = Property(status, "windows");
        var charge = Property(status, "charge");
        var lighting = Property(status, "lighting");

        var items = new List<VehicleStatusItems>();

        var (mileage, mileageUnit) = SplitNumberUnit(status, "mileage");
        AddNonNegativeNumber(items, "2103010", mileage, mileageUnit);

        var (range, rangeUnit) = SplitNumberUnit(status, "batteryPreMileage");
        AddNonNegativeNumber(items, "2011501", range, rangeUnit);

        var (fuelRange, fuelRangeUnit) = SplitNumberUnit(status, "preMileage");
        AddNonNegativeNumber(items, "2011007", fuelRange, fuelRangeUnit);

        var (oil, oilUnit) = SplitNumberUnit(status, "remainOil");
        AddNonNegativeNumber(items, "2017002", oil, oilUnit);

        var (soc, socUnit) = SplitNumberUnit(status, "powerBatteryDisplayVal");
        AddPercentage(items, "2013021", soc, socUnit);

        // BeanTech reports the displayed SOC and the BMS usable charge separately.
        // Keep this value out of 2041301, which is SOCE/SOH on existing platforms.
        var (remainingUsable, remainingUsableUnit) = SplitNumberUnit(status, "powerBatteryPercent");
        AddPercentage(items, "9000025", remainingUsable, remainingUsableUnit);

        var (auxBattery, auxBatteryUnit) = SplitNumberUnit(status, "remainElectricPercent");
        AddPercentage(items, "9000024", auxBattery, auxBatteryUnit);

        var (tpLf, tpLfUnit) = SplitNumberUnit(tirePress, "lfTirePressVal");
        Add(items, "2101001", tpLf, tpLfUnit);
        var (tpRf, tpRfUnit) = SplitNumberUnit(tirePress, "rfTirePressVal");
        Add(items, "2101002", tpRf, tpRfUnit);
        var (tpLb, tpLbUnit) = SplitNumberUnit(tirePress, "lbTirePressVal");
        Add(items, "2101003", tpLb, tpLbUnit);
        var (tpRb, tpRbUnit) = SplitNumberUnit(tirePress, "rbTirePressVal");
        Add(items, "2101004", tpRb, tpRbUnit);

        var (ttLf, ttLfUnit) = SplitNumberUnit(tireTemp, "lfTireTempVal");
        Add(items, "2101005", ttLf, ttLfUnit);
        var (ttRf, ttRfUnit) = SplitNumberUnit(tireTemp, "rfTireTempVal");
        Add(items, "2101006", ttRf, ttRfUnit);
        var (ttLb, ttLbUnit) = SplitNumberUnit(tireTemp, "lbTireTempVal");
        Add(items, "2101007", ttLb, ttLbUnit);
        var (ttRb, ttRbUnit) = SplitNumberUnit(tireTemp, "rbTireTempVal");
        Add(items, "2101008", ttRb, ttRbUnit);

        Add(items, "2102001", Value(tirePress, "lfTirePressSts"));
        Add(items, "2102002", Value(tirePress, "rfTirePressSts"));
        Add(items, "2102003", Value(tirePress, "lbTirePressSts"));
        Add(items, "2102004", Value(tirePress, "rbTirePressSts"));

        Add(items, "2102007", Value(tireTemp, "lfTireTempSts"));
        Add(items, "2102008", Value(tireTemp, "rfTireTempSts"));
        Add(items, "2102009", Value(tireTemp, "lbTireTempSts"));
        Add(items, "2102010", Value(tireTemp, "rbTireTempSts"));

        Add(items, "2208001", Value(door, "mainDrveDoorLockSts"));
        Add(items, "2206002", Value(door, "mainDrveDoorSts"));
        Add(items, "2206004", Value(door, "viceDoorSts"));
        Add(items, "2206003", Value(door, "lbDoorSts"));
        Add(items, "2206005", Value(door, "rbDoorSts"));
        Add(items, "2206001", Value(door, "tailgateOpenUpSts"));

        Add(items, "2210001", WindowClosedCode(windows, "lfWinPosnSts"));
        Add(items, "2210002", WindowClosedCode(windows, "rfWinPosnSts"));
        // The snapshot contract uses 2210004 for the rear driver side and
        // 2210003 for the rear passenger side. China vehicles are left-hand drive.
        Add(items, "2210004", WindowClosedCode(windows, "lbWinPosnSts"));
        Add(items, "2210003", WindowClosedCode(windows, "rbWinPosnSts"));
        Add(items, "2210005", Value(windows, "skyLightSts"));

        Add(items, "2220001", Value(seat, "mainDriverSeatHeatSts"));
        Add(items, "2220002", Value(seat, "viceSeatHeatSts"));
        Add(items, "2220003", Value(seat, "mainDriverSeatVentSts"));
        Add(items, "2220004", Value(seat, "viceSeatVentSts"));

        Add(items, "2016001", Value(status, "engineSts"));
        Add(items, "2202001", Value(status, "airConditionSts"));
        Add(items, "2041142", Value(charge, "chargeStatus"));
        Add(items, "2042082", Value(charge, "chargingGunStatus"));
        var (chargingTime, chargingTimeUnit) = SplitNumberUnit(charge, "chargingTime");
        AddNonNegativeNumber(items, "2013022", chargingTime, chargingTimeUnit ?? "min");

        Add(items, "2210011", Value(windows, "lfWinLearnSts"));
        Add(items, "2210010", Value(windows, "rfWinLearnSts"));
        Add(items, "2210013", Value(windows, "lbWinLearnSts"));
        Add(items, "2210012", Value(windows, "rbWinLearnSts"));

        Add(items, "2210032", Value(status, "backFrost"));
        Add(items, "2222001", Value(status, "frontFrost"));
        Add(items, "2060016", Value(status, "steerWheelHeatdSts"));

        var (inCarTemp, _) = SplitNumberUnit(status, "inCarTemperature");
        if (double.TryParse(inCarTemp, NumberStyles.Float, CultureInfo.InvariantCulture, out var tempC))
        {
            Add(items, "2201001", (tempC * 10).ToString("0", CultureInfo.InvariantCulture));
        }

        // BeanTech-only lighting signals.
        Add(items, "9000001", Value(lighting, "nearBeamSts"));
        Add(items, "9000002", Value(lighting, "farBeamSts"));
        Add(items, "9000003", Value(lighting, "leftTurnLampSts"));
        Add(items, "9000004", Value(lighting, "rightTurnLampSts"));

        // BeanTech-only body and state signals.
        Add(items, "9000005", Value(status, "oilAlarmSts"));
        Add(items, "9000006", Value(status, "engineDoorSts"));
        Add(items, "9000007", Value(status, "acAutoModeSts"));
        Add(items, "9000008", Value(status, "airCleanSts"));
        Add(items, "9000009", Value(status, "cabinClean"));
        Add(items, "9000010", Value(door, "backDoorSts"));

        // BeanTech-only charging and diagnostic signals.
        var (chargeSoc, chargeSocUnit) = SplitNumberUnit(charge, "chargeSoc");
        AddPercentage(items, "9000011", chargeSoc, chargeSocUnit ?? "%");
        Add(items, "9000012", Value(charge, "chargingGunModel"));
        Add(items, "9000013", Value(status, "hcuPowertrainSts"));
        Add(items, "9000014", Value(status, "power"));
        Add(items, "9000015", Value(status, "batteryPackSts"));
        Add(items, "9000016", Value(status, "accBnClnOff"));
        Add(items, "9000017", Value(status, "tboxState"));
        Add(items, "9000018", Value(status, "wirelessLevel"));
        Add(items, "9000019", Value(status, "oilQty"));

        // BeanTech-only tire warning signals.
        Add(items, "9000020", Value(tirePress, "lfTirePressIndcrSts"));
        Add(items, "9000021", Value(tirePress, "rfTirePressIndcrSts"));
        Add(items, "9000022", Value(tirePress, "lbTirePressIndcrSts"));
        Add(items, "9000023", Value(tirePress, "rbTirePressIndcrSts"));

        var latitude = Double(Property(data, "latitude"));
        var longitude = Double(Property(data, "longitude"));
        if (latitude.HasValue && longitude.HasValue)
        {
            Add(items, "2310001", "1");
        }

        var updateTime = Long(Property(data, "updateTime")) ?? 0;

        return new VehicleStatus
        {
            AcquisitionTime = Long(Property(data, "acquisitionTime")) ?? 0,
            UpdateTime = updateTime,
            UploadTime = updateTime,
            DeviceId = FirstNonEmpty(Value(data, "deviceId"), vehicle.VehicleId, vehicle.Vin),
            Latitude = latitude,
            Longitude = longitude,
            Items = items.ToArray(),
            GlobalStatusList = Array.Empty<object>()
        };
    }

    private static (string? Value, string? Unit) SplitNumberUnit(JsonNode? node, string property)
    {
        var raw = Value(node, property);
        if (String.IsNullOrWhiteSpace(raw))
        {
            return (null, null);
        }

        var comma = raw.IndexOf(',');
        return comma < 0
            ? (raw.Trim(), null)
            : (raw[..comma].Trim(), raw[(comma + 1)..].Trim());
    }

    private static string? WindowClosedCode(JsonNode? node, string property)
    {
        return Integer(Property(node, property)) switch
        {
            5 => "1",
            >= 0 and <= 4 => "0",
            _ => null
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

    private static void AddNonNegativeNumber(
        List<VehicleStatusItems> items,
        string code,
        string? value,
        string? unit = null)
    {
        if (System.Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && System.Double.IsFinite(number)
            && number >= 0)
        {
            Add(items, code, number.ToString(CultureInfo.InvariantCulture), unit);
        }
    }

    private static void AddPercentage(
        List<VehicleStatusItems> items,
        string code,
        string? value,
        string? unit = null)
    {
        if (System.Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && System.Double.IsFinite(number)
            && number is >= 0 and <= 100)
        {
            Add(items, code, number.ToString(CultureInfo.InvariantCulture), unit);
        }
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
