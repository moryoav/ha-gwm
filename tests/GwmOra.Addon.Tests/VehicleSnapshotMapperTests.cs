using GwmOra.Addon.Gwm;
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
            AppShowSeriesName = "ORA",
            BrandName = "GWM",
            Vtype = "Funky Cat"
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
                Item("2103010", 12345, "km"),
                Item("2201001", 234, "C"),
                Item("2202001", 1, null),
                Item("2208001", 0, null),
                Item("2210001", 1, null),
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
        Assert.Equal(12345, snapshot.Values.OdometerKm);
        Assert.Equal(23.4, snapshot.Values.InteriorTemperatureC);
        Assert.True(snapshot.Values.AcActive);
        Assert.True(snapshot.Values.Locked);
        Assert.False(snapshot.Values.WindowFrontLeftOpen);
        Assert.True(snapshot.Values.ChargePlugConnected);
        Assert.Equal("cool", snapshot.Climate.Mode);
        Assert.Equal(23, snapshot.Climate.TargetTemperatureC);
        Assert.Equal(15, snapshot.Climate.OperationTimeMinutes);
        Assert.NotNull(snapshot.Location);
        Assert.True(snapshot.Capabilities.RemoteCommands);
        Assert.Equal("80", snapshot.RawItems["2013021"].Value);
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

    private static VehicleStatusItems Item(string code, object value, string? unit)
    {
        return new VehicleStatusItems
        {
            Code = code,
            Value = value,
            Unit = unit
        };
    }
}
