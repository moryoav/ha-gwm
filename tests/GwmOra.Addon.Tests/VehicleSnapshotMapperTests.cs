using System.Text.Json;
using GwmOra.Addon.Gwm;
using GwmOra.Addon.Models;
using libgwmapi.DTO.Vehicle;

namespace GwmOra.Addon.Tests;

public class VehicleSnapshotMapperTests
{
    [Fact]
    public void MapConvertsKnownVehicleValues()
    {
        var vehicle = new Vehicle
        {
            Vin = "VIN123",
            AppShowSeriesName = "Vehicle series",
            BrandName = "GWM",
            Vtype = "Vehicle model"
        };
        var status = new VehicleStatus
        {
            AcquisitionTime = 1_700_000_000_000,
            UpdateTime = 1_700_000_100_000,
            DeviceId = "device",
            Latitude = 32.1,
            Longitude = 34.8,
            Items =
            [
                Item("2013021", 80, "%"),
                Item("2011501", 210, "km"),
                Item("2017002", 45, "L"),
                Item("2011007", 418, "km"),
                Item("2103010", 12345, "km"),
                Item("2201001", 234, "C"),
                Item("2202001", 1, null),
                Item("2208001", 0, null),
                Item("2210001", 1, null),
                Item("2210002", 0, null),
                Item("2210003", 2, null),
                Item("2210004", 1, null),
                Item("2041142", 1, null),
                Item("2206001", 0, null),
                Item("2206002", 1, null),
                Item("2206003", 0, null),
                Item("2206004", 0, null),
                Item("2206005", 1, null),
                Item("2210005", 3, null),
                Item("2210032", 1, null),
                Item("2310001", 1, null),
                Item("2102001", 1, null),
                Item("2102002", 0, null),
                Item("2102003", 2, null),
                Item("2102004", 3, null),
                Item("2102007", 4, null),
                Item("2102008", 5, null),
                Item("2102009", 6, null),
                Item("2102010", 7, null),
                Item("2210011", 0, null),
                Item("2210010", 1, null),
                Item("2210013", 2, null),
                Item("2210012", 3, null),
                Item("2060016", 1, null),
                Item("2424001", 2, null),
                Item("2424002", 3, null),
                Item("2202111", 1, null),
                Item("2016001", 2, null),
                Item("2220001", 3, null),
                Item("2220002", 2, null),
                Item("2220003", 3, null),
                Item("2220004", 1, null),
                Item("2042082", 1, null)
            ]
        };
        var basics = new VehicleBasicsInfo
        {
            Config = new VehicleConfig
            {
                AirConditionerTemperature = "23",
                AirConditionerStatusTime = "900"
            }
        };

        var snapshot = VehicleSnapshotMapper.Map(vehicle, status, basics, true, "ok");

        Assert.Equal("VIN123", snapshot.Vin);
        Assert.Equal(80, snapshot.Values.Soc);
        Assert.Equal(210, snapshot.Values.RangeKm);
        Assert.Equal(45, snapshot.Values.FuelLevelL);
        Assert.Equal(418, snapshot.Values.FuelRangeKm);
        Assert.Equal(12345, snapshot.Values.OdometerKm);
        Assert.Equal(23.4, snapshot.Values.InteriorTemperatureC);
        Assert.True(snapshot.Values.AcActive);
        Assert.True(snapshot.Values.Locked);
        Assert.Equal("charging", snapshot.Values.ChargingStatus);
        Assert.True(snapshot.Values.ChargingActive);
        Assert.False(snapshot.Values.WindowFrontLeftOpen);
        Assert.False(snapshot.Values.WindowFrontDriverOpen);
        Assert.True(snapshot.Values.WindowFrontPassengerOpen);
        Assert.False(snapshot.Values.WindowRearDriverSideOpen);
        Assert.True(snapshot.Values.WindowRearPassengerSideOpen);
        Assert.False(snapshot.Values.TrunkOpen);
        Assert.True(snapshot.Values.DoorFrontDriverOpen);
        Assert.False(snapshot.Values.DoorFrontPassengerOpen);
        Assert.False(snapshot.Values.DoorRearDriverSideOpen);
        Assert.True(snapshot.Values.DoorRearPassengerSideOpen);
        Assert.Equal(3, snapshot.Values.SunroofPositionCode);
        Assert.True(snapshot.Values.RearDefroster);
        Assert.True(snapshot.Values.GpsAuthorized);
        Assert.Equal(1, snapshot.Values.TirePressureStateFrontLeft);
        Assert.Equal(0, snapshot.Values.TirePressureStateFrontRight);
        Assert.Equal(2, snapshot.Values.TirePressureStateRearLeft);
        Assert.Equal(3, snapshot.Values.TirePressureStateRearRight);
        Assert.Equal(4, snapshot.Values.TireTemperatureStateFrontLeft);
        Assert.Equal(5, snapshot.Values.TireTemperatureStateFrontRight);
        Assert.Equal(6, snapshot.Values.TireTemperatureStateRearLeft);
        Assert.Equal(7, snapshot.Values.TireTemperatureStateRearRight);
        Assert.Equal(0, snapshot.Values.WindowLearnFrontLeft);
        Assert.Equal(1, snapshot.Values.WindowLearnFrontRight);
        Assert.Equal(2, snapshot.Values.WindowLearnRearLeft);
        Assert.Equal(3, snapshot.Values.WindowLearnRearRight);
        Assert.True(snapshot.Values.SteeringWheelHeaterActive);
        Assert.Equal(2, snapshot.Values.RearLeftSeatHeaterLevel);
        Assert.Equal(3, snapshot.Values.RearRightSeatHeaterLevel);
        Assert.True(snapshot.Values.FrontWindscreenHeaterActive);
        Assert.Equal(2, snapshot.Values.EngineStateCode);
        Assert.Equal(3, snapshot.Values.FrontDriverSeatHeaterLevel);
        Assert.Equal(2, snapshot.Values.FrontPassengerSeatHeaterLevel);
        Assert.Equal(3, snapshot.Values.FrontDriverSeatVentLevel);
        Assert.Equal(1, snapshot.Values.FrontPassengerSeatVentLevel);
        Assert.True(snapshot.Values.ChargePlugConnected);
        Assert.Equal("cool", snapshot.Climate.Mode);
        Assert.Equal(23, snapshot.Climate.TargetTemperatureC);
        Assert.Equal(15, snapshot.Climate.OperationTimeMinutes);
        Assert.NotNull(snapshot.Location);
        Assert.True(snapshot.Capabilities.RemoteCommands);
        Assert.Equal("80", snapshot.RawItems["2013021"].Value);
    }

    [Fact]
    public void MapHandlesVehiclesWithoutOptionalSignals()
    {
        var snapshot = VehicleSnapshotMapper.Map(
            new Vehicle { Vin = "VIN123" },
            new VehicleStatus { Items = Array.Empty<VehicleStatusItems>() },
            new VehicleBasicsInfo(),
            false,
            "idle");

        foreach (var property in typeof(VehicleValues).GetProperties())
        {
            Assert.Null(property.GetValue(snapshot.Values));
        }

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        Assert.Contains("\"front_driver_seat_vent_level\":null", json);
        Assert.Empty(snapshot.RawItems);
    }

    [Fact]
    public void MapIgnoresMalformedItemsAndUsesLatestNonNullDuplicate()
    {
        var status = new VehicleStatus
        {
            AcquisitionTime = Int64.MaxValue,
            UpdateTime = Int64.MaxValue,
            Latitude = Double.NaN,
            Longitude = 181,
            Items =
            [
                null!,
                Item(null, 1, null),
                Item("2013021", Double.NaN, "%"),
                Item("2011501", "Infinity", "km"),
                Item("2017002", -1, "L"),
                Item("2011007", 100, "km"),
                Item("2011007", 200, "km"),
                Item("2011007", null, "km"),
                Item("2206002", JsonValue("1.0"), null),
                Item("2206004", "unknown", null),
                Item("2210001", "unknown", null),
                Item("2102001", 1.5, null),
                Item("2220001", 4, null),
                Item("2220002", JsonValue("3.0"), null),
                Item("2220003", -1, null)
            ]
        };

        var snapshot = VehicleSnapshotMapper.Map(
            new Vehicle { Vin = "VIN123" },
            status,
            new VehicleBasicsInfo(),
            false,
            "idle");

        Assert.Null(snapshot.Values.Soc);
        Assert.Null(snapshot.Values.RangeKm);
        Assert.Null(snapshot.Values.FuelLevelL);
        Assert.Equal(200, snapshot.Values.FuelRangeKm);
        Assert.True(snapshot.Values.DoorFrontDriverOpen);
        Assert.Null(snapshot.Values.DoorFrontPassengerOpen);
        Assert.Null(snapshot.Values.WindowFrontDriverOpen);
        Assert.Null(snapshot.Values.TirePressureStateFrontLeft);
        Assert.Null(snapshot.Values.FrontDriverSeatHeaterLevel);
        Assert.Equal(3, snapshot.Values.FrontPassengerSeatHeaterLevel);
        Assert.Null(snapshot.Values.FrontDriverSeatVentLevel);
        Assert.Null(snapshot.Timestamps.AcquisitionTime);
        Assert.Null(snapshot.Timestamps.UpdateTime);
        Assert.Null(snapshot.Location);
        Assert.Equal("200", snapshot.RawItems["2011007"].Value);
        Assert.DoesNotContain(snapshot.RawItems, item => String.IsNullOrWhiteSpace(item.Key));

        var exception = Record.Exception(() => JsonSerializer.Serialize(
            new VehiclesResponse { Vehicles = [snapshot] },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(4, null)]
    public void MapAcceptsOnlyDocumentedSeatLevels(int rawLevel, int? expected)
    {
        var snapshot = VehicleSnapshotMapper.Map(
            new Vehicle { Vin = "VIN123" },
            new VehicleStatus { Items = [Item("2220003", rawLevel, null)] },
            new VehicleBasicsInfo(),
            false,
            "idle");

        Assert.Equal(expected, snapshot.Values.FrontDriverSeatVentLevel);
    }

    [Theory]
    [InlineData(0, 0, "disconnected", false)]
    [InlineData(0, 1, "connected", false)]
    [InlineData(1, 1, "charging", true)]
    [InlineData(2, 1, "awaiting_charging", false)]
    [InlineData(5, 1, "waiting_for_power", false)]
    [InlineData(6, 1, "error", false)]
    [InlineData(7, 1, null, null)]
    public void MapConvertsChargeStatusCodes(
        int chargeStatus,
        int plugStatus,
        string? expectedStatus,
        bool? expectedChargingActive)
    {
        var status = new VehicleStatus
        {
            Items =
            [
                Item("2041142", chargeStatus, null),
                Item("2042082", plugStatus, null)
            ]
        };

        var snapshot = VehicleSnapshotMapper.Map(
            new Vehicle { Vin = "VIN123" },
            status,
            new VehicleBasicsInfo(),
            false,
            "idle");

        Assert.Equal(expectedStatus, snapshot.Values.ChargingStatus);
        Assert.Equal(expectedChargingActive, snapshot.Values.ChargingActive);
    }

    [Theory]
    [InlineData("300", 5)]
    [InlineData("900", 15)]
    [InlineData("1800", 30)]
    [InlineData("30", 30)]
    [InlineData("25", 25)]
    public void NormalizeOperationTimeConvertsCloudSeconds(string? value, int expected)
    {
        Assert.Equal(
            expected,
            VehicleSnapshotMapper.NormalizeOperationTime(value, VehicleSnapshotMapper.DefaultOperationTimeMinutes));
    }

    [Theory]
    [InlineData("200")]
    [InlineData("301")]
    [InlineData("invalid")]
    [InlineData(null)]
    public void NormalizeOperationTimeUsesProvidedFallbackForInvalidValues(string? value)
    {
        Assert.Equal(17, VehicleSnapshotMapper.NormalizeOperationTime(value, 17));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(15, true)]
    [InlineData(30, true)]
    [InlineData(4, false)]
    [InlineData(16, true)]
    [InlineData(31, false)]
    public void OperationTimeValidationUsesSupportedRange(int value, bool expected)
    {
        Assert.Equal(expected, VehicleSnapshotMapper.IsValidOperationTime(value));
    }

    [Theory]
    [InlineData("16", true, 16)]
    [InlineData("32", true, 32)]
    [InlineData("15", false, 0)]
    [InlineData("33", false, 0)]
    [InlineData(null, false, 0)]
    public void TryGetValidTemperatureRejectsMissingAndOutOfRangeValues(
        string? value,
        bool expectedResult,
        int expectedTemperature)
    {
        var result = VehicleSnapshotMapper.TryGetValidTemperature(value, out var temperature);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedTemperature, temperature);
    }

    private static JsonElement JsonValue(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static VehicleStatusItems Item(string? code, object? value, string? unit)
    {
        return new VehicleStatusItems
        {
            Code = code!,
            Value = value!,
            Unit = unit
        };
    }
}
