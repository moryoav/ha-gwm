using GwmOra.Addon.Models;
using GwmOra.Addon.RemoteCommands;

namespace GwmOra.Addon.Tests;

public class RemoteCommandServiceTests
{
    [Fact]
    public void ClimateValidationAcceptsRunTimeOnly()
    {
        RemoteCommandService.ValidateClimateRequest(new ClimateCommandRequest
        {
            OperationTimeMinutes = 16
        });
    }

    [Theory]
    [InlineData(4)]
    [InlineData(31)]
    public void ClimateValidationRejectsUnsupportedRunTime(int operationTimeMinutes)
    {
        var request = new ClimateCommandRequest
        {
            OperationTimeMinutes = operationTimeMinutes
        };

        Assert.Throws<ArgumentException>(() => RemoteCommandService.ValidateClimateRequest(request));
    }

    [Fact]
    public void ClimateValidationRejectsEmptyRequest()
    {
        Assert.Throws<ArgumentException>(
            () => RemoteCommandService.ValidateClimateRequest(new ClimateCommandRequest()));
    }

    [Fact]
    public void RunTimeOnlyDoesNotSendVehicleCommand()
    {
        var request = new ClimateCommandRequest
        {
            OperationTimeMinutes = 15
        };

        Assert.False(RemoteCommandService.ShouldSendClimateCommand(request, currentlyOn: false));
        Assert.False(RemoteCommandService.ShouldSendClimateCommand(request, currentlyOn: true));
    }

    [Fact]
    public void TemperatureOnlySendsVehicleCommandOnlyWhenClimateIsOn()
    {
        var request = new ClimateCommandRequest
        {
            Temperature = 22
        };

        Assert.False(RemoteCommandService.ShouldSendClimateCommand(request, currentlyOn: false));
        Assert.True(RemoteCommandService.ShouldSendClimateCommand(request, currentlyOn: true));
    }

    [Theory]
    [InlineData("cool")]
    [InlineData("off")]
    public void ExplicitModeAlwaysSendsVehicleCommand(string mode)
    {
        var request = new ClimateCommandRequest
        {
            Mode = mode
        };

        Assert.True(RemoteCommandService.ShouldSendClimateCommand(request, currentlyOn: false));
    }
}
