using GwmOra.Addon.Models;
using GwmOra.Addon.RemoteCommands;
using libgwmapi.DTO.Vehicle;

namespace GwmOra.Addon.Tests;

public class RemoteCommandServiceTests
{
    [Fact]
    public void PendingRemoteCommandResultUsesNeutralProgressMessage()
    {
        var result = new RemoteCtrlResultT5
        {
            ResultCode = "2000",
            ResultMsg = "Failed. The command is in process, try again"
        };

        var message = RemoteCommandService.FormatRemoteCommandResult("Door lock", result, 1);

        Assert.Equal("Door lock: in progress (1/18) - waiting for vehicle result [2000]", message);
    }

    [Fact]
    public void RussianResultCodeOneThousandRemainsPending()
    {
        var result = new RemoteCtrlResultT5
        {
            ResultCode = "1000",
            ResultMsg = "Processing"
        };

        var message = RemoteCommandService.FormatRemoteCommandResult(
            "Door lock",
            result,
            1,
            isRussianRegion: true,
            maxResultPolls: 60);

        Assert.True(RemoteCommandService.IsPendingRemoteCommandResult(result, isRussianRegion: true));
        Assert.False(RemoteCommandService.IsPendingRemoteCommandResult(result, isRussianRegion: false));
        Assert.Equal("Door lock: in progress (1/60) - waiting for vehicle result [1000]", message);
    }

    [Fact]
    public void RussianResultSelectionUsesExpectedRemoteTypeAndPrioritizesSuccess()
    {
        var request = RemoteCommandFactory.CreateLockCommand(
            "VIN123",
            "hash",
            lockVehicle: true,
            useRussianProtocol: true);
        var unrelatedSuccess = new RemoteCtrlResultT5
        {
            RemoteType = "0x04",
            ResultCode = "0"
        };
        var matchingFailure = new RemoteCtrlResultT5
        {
            RemoteType = "0x05",
            ResultCode = "11"
        };
        var matchingSuccess = new RemoteCtrlResultT5
        {
            RemoteType = "0x05",
            ResultCode = "6"
        };

        var selected = RemoteCommandService.SelectRemoteCommandResult(
            new[] { unrelatedSuccess, matchingFailure, matchingSuccess },
            request,
            isRussianRegion: true);

        Assert.Same(matchingSuccess, selected);
    }

    [Fact]
    public void RussianResultSelectionRetriesCodeElevenBeforeHardFailure()
    {
        var request = RemoteCommandFactory.CreateWindowCloseCommand(
            "VIN123",
            "hash",
            useRussianProtocol: true);
        var hardFailure = new RemoteCtrlResultT5
        {
            RemoteType = "0x08",
            ResultCode = "9"
        };
        var intermediate = new RemoteCtrlResultT5
        {
            RemoteType = "0x08",
            ResultCode = "11"
        };

        var selected = RemoteCommandService.SelectRemoteCommandResult(
            new[] { hardFailure, intermediate },
            request,
            isRussianRegion: true);

        Assert.Same(intermediate, selected);
    }

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
