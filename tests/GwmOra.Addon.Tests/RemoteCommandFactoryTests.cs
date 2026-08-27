using System.Text.Json;
using GwmOra.Addon.RemoteCommands;

namespace GwmOra.Addon.Tests;

public class RemoteCommandFactoryTests
{
    [Fact]
    public void ClimateDefaultsSerializeRunTimeInSeconds()
    {
        var defaults = RemoteCommandFactory.CreateClimateDefaults("VIN123", 22, 15);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(defaults));

        Assert.Equal("22", document.RootElement.GetProperty("airConditionerTemperature").GetString());
        Assert.Equal("900", document.RootElement.GetProperty("airConditionerTime").GetString());
    }

    [Fact]
    public void ClimateCommandSerializesRunTimeInMinutes()
    {
        var command = RemoteCommandFactory.CreateClimateCommand("VIN123", "hash", "1", 22, 15);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(command));
        var climate = document.RootElement
            .GetProperty("instructions")
            .GetProperty("0x04")
            .GetProperty("airConditioner");

        Assert.Equal("15", climate.GetProperty("operationTime").GetString());
        Assert.Equal("1", climate.GetProperty("switchOrder").GetString());
        Assert.Equal("22", climate.GetProperty("temperature").GetString());
    }

    [Fact]
    public void LockCommandSerializesOnlyLockInstruction()
    {
        var command = RemoteCommandFactory.CreateLockCommand("VIN123", "hash", true);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(command));
        var instructions = document.RootElement.GetProperty("instructions");

        Assert.True(instructions.TryGetProperty("0x05", out var x05));
        Assert.False(instructions.TryGetProperty("0x04", out _));
        Assert.False(instructions.TryGetProperty("0x08", out _));
        Assert.Equal("2", x05.GetProperty("switchOrder").GetString());
    }

    [Fact]
    public void WindowCloseTargetsAllSideWindows()
    {
        var command = RemoteCommandFactory.CreateWindowCloseCommand("VIN123", "hash");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(command));
        var window = document.RootElement
            .GetProperty("instructions")
            .GetProperty("0x08")
            .GetProperty("window");

        Assert.Equal("0", window.GetProperty("leftFront").GetString());
        Assert.Equal("0", window.GetProperty("leftBack").GetString());
        Assert.Equal("0", window.GetProperty("rightFront").GetString());
        Assert.Equal("0", window.GetProperty("rightBack").GetString());
    }

    [Fact]
    public void RussianCommandsUseTypeThree()
    {
        var climate = RemoteCommandFactory.CreateClimateCommand(
            "VIN123",
            "hash",
            "1",
            22,
            15,
            useRussianProtocol: true);
        var doorLock = RemoteCommandFactory.CreateLockCommand(
            "VIN123",
            "hash",
            lockVehicle: true,
            useRussianProtocol: true);
        var windowClose = RemoteCommandFactory.CreateWindowCloseCommand(
            "VIN123",
            "hash",
            useRussianProtocol: true);

        Assert.Equal(3, climate.Type);
        Assert.Equal(3, doorLock.Type);
        Assert.Equal(3, windowClose.Type);
    }

    [Fact]
    public void RussianWindowCloseUsesRussianSwitchOrderAndOmitsSunroof()
    {
        var command = RemoteCommandFactory.CreateWindowCloseCommand(
            "VIN123",
            "hash",
            useRussianProtocol: true);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(command));
        var windowInstruction = document.RootElement
            .GetProperty("instructions")
            .GetProperty("0x08");

        Assert.Equal("2", windowInstruction.GetProperty("switchOrder").GetString());
        Assert.False(windowInstruction.GetProperty("window").TryGetProperty("skyLight", out _));
    }

    [Fact]
    public void StandardWindowClosePayloadRemainsUnchanged()
    {
        var command = RemoteCommandFactory.CreateWindowCloseCommand("VIN123", "hash");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(command));
        var windowInstruction = document.RootElement
            .GetProperty("instructions")
            .GetProperty("0x08");

        Assert.Equal(2, command.Type);
        Assert.Equal("0", windowInstruction.GetProperty("switchOrder").GetString());
        Assert.Equal(String.Empty, windowInstruction.GetProperty("window").GetProperty("skyLight").GetString());
    }

    [Fact]
    public void ChinaDescriptorIsProcessLocalAndDoesNotChangeSerializedPayloads()
    {
        var command = RemoteCommandFactory.CreateChinaCommand(
            "VIN123",
            libgwmapi.DTO.Vehicle.ChinaRemoteCommandKind.SunroofOpen,
            29,
            openAngle: 2);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(command));

        Assert.False(document.RootElement.TryGetProperty("chinaCommand", out _));
        Assert.Equal(29, command.ChinaCommand.CommandCode);
        Assert.Equal(2, command.ChinaCommand.OpenAngle);
    }
}
