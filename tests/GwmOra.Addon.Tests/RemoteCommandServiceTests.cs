using GwmOra.Addon.Configuration;
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
    public void RussianResultSelectionPrefersCurrentSequenceOverStaleSameTypeSuccess()
    {
        var request = RemoteCommandFactory.CreateLockCommand(
            "VIN123",
            "hash",
            lockVehicle: true,
            useRussianProtocol: true);
        var staleSuccess = new RemoteCtrlResultT5
        {
            HwCommandId = "OLD-SEQUENCE",
            RemoteType = "0x05",
            ResultCode = "0"
        };
        var currentPending = new RemoteCtrlResultT5
        {
            HwCommandId = request.SeqNo,
            RemoteType = "0x05",
            ResultCode = "1000"
        };

        var selected = RemoteCommandService.SelectRemoteCommandResult(
            new[] { staleSuccess, currentPending },
            request,
            isRussianRegion: true);

        Assert.Same(currentPending, selected);
    }

    [Theory]
    [InlineData("11")]
    [InlineData("9")]
    public void RussianResultSelectionKeepsCurrentSequenceResultOverStaleSuccess(string currentResultCode)
    {
        var request = RemoteCommandFactory.CreateLockCommand(
            "VIN123",
            "hash",
            lockVehicle: true,
            useRussianProtocol: true);
        var staleSuccess = new RemoteCtrlResultT5
        {
            HwCommandId = "OLD-SEQUENCE",
            RemoteType = "0x05",
            ResultCode = "0"
        };
        var currentResult = new RemoteCtrlResultT5
        {
            HwCommandId = request.SeqNo,
            RemoteType = "0x05",
            ResultCode = currentResultCode
        };

        var selected = RemoteCommandService.SelectRemoteCommandResult(
            new[] { staleSuccess, currentResult },
            request,
            isRussianRegion: true);

        Assert.Same(currentResult, selected);
    }

    [Fact]
    public void RussianResultSelectionPrefersMissingSequenceFallbackOverStaleSequence()
    {
        var request = RemoteCommandFactory.CreateLockCommand(
            "VIN123",
            "hash",
            lockVehicle: true,
            useRussianProtocol: true);
        var staleSuccess = new RemoteCtrlResultT5
        {
            HwCommandId = "OLD-SEQUENCE",
            RemoteType = "0x05",
            ResultCode = "0"
        };
        var currentPendingWithoutSequence = new RemoteCtrlResultT5
        {
            HwCommandId = null!,
            RemoteType = "0x05",
            ResultCode = "1000"
        };

        var selected = RemoteCommandService.SelectRemoteCommandResult(
            new[] { staleSuccess, currentPendingWithoutSequence },
            request,
            isRussianRegion: true);

        Assert.Same(currentPendingWithoutSequence, selected);
    }

    [Fact]
    public void RussianResultSelectionIgnoresOnlyUnrelatedRemoteTypes()
    {
        var request = RemoteCommandFactory.CreateWindowCloseCommand(
            "VIN123",
            "hash",
            useRussianProtocol: true);
        var unrelatedSuccess = new RemoteCtrlResultT5
        {
            RemoteType = "0x05",
            ResultCode = "0"
        };

        var selected = RemoteCommandService.SelectRemoteCommandResult(
            new[] { unrelatedSuccess },
            request,
            isRussianRegion: true);

        Assert.Null(selected);
    }

    [Fact]
    public void RemoteResultSelectionToleratesMissingResultsAndNullItems()
    {
        var request = RemoteCommandFactory.CreateWindowCloseCommand(
            "VIN123",
            "hash",
            useRussianProtocol: true);
        var currentResult = new RemoteCtrlResultT5
        {
            HwCommandId = request.SeqNo,
            RemoteType = "0x08",
            ResultCode = "6"
        };

        Assert.Null(RemoteCommandService.SelectRemoteCommandResult(null, request, isRussianRegion: true));
        Assert.Null(RemoteCommandService.SelectRemoteCommandResult(
            Array.Empty<RemoteCtrlResultT5?>(),
            request,
            isRussianRegion: true));
        Assert.Null(RemoteCommandService.SelectRemoteCommandResult(
            new RemoteCtrlResultT5?[] { null },
            request,
            isRussianRegion: true));
        Assert.Same(
            currentResult,
            RemoteCommandService.SelectRemoteCommandResult(
                new RemoteCtrlResultT5?[] { null, currentResult },
                request,
                isRussianRegion: true));
    }

    [Fact]
    public void NonRussianResultSelectionKeepsExistingSequenceAndFallbackBehavior()
    {
        var request = RemoteCommandFactory.CreateWindowCloseCommand(
            "VIN123",
            "hash");
        var firstResult = new RemoteCtrlResultT5 { HwCommandId = "OTHER", ResultCode = "0" };
        var exactResult = new RemoteCtrlResultT5 { HwCommandId = request.SeqNo, ResultCode = "2000" };

        Assert.Same(
            exactResult,
            RemoteCommandService.SelectRemoteCommandResult(
                new[] { firstResult, exactResult },
                request,
                isRussianRegion: false));
        Assert.Same(
            firstResult,
            RemoteCommandService.SelectRemoteCommandResult(
                new[] { firstResult },
                request,
                isRussianRegion: false));
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
    [InlineData("cool")]
    [InlineData("heat")]
    [InlineData("off")]
    public void ClimateValidationAcceptsSupportedModes(string mode)
    {
        RemoteCommandService.ValidateClimateRequest(new ClimateCommandRequest
        {
            Mode = mode
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

    [Fact]
    public void ChargingPlanValidationAcceptsFiveMinuteWindow()
    {
        RemoteCommandService.ValidateChargingPlanRequest("VIN123", new ChargingPlanRequest
        {
            Enable = true,
            StartTime = 0,
            EndTime = 300_000,
            PlanType = 0
        });
    }

    [Fact]
    public void ChargingPlanValidationAcceptsClearWithoutWindow()
    {
        RemoteCommandService.ValidateChargingPlanRequest("VIN123", new ChargingPlanRequest
        {
            Enable = false
        });
    }

    [Fact]
    public void ChargingPlanValidationRejectsMissingEnable()
    {
        Assert.Throws<ArgumentException>(() =>
            RemoteCommandService.ValidateChargingPlanRequest("VIN123", new ChargingPlanRequest()));
    }

    [Fact]
    public void ChargingPlanValidationRejectsMissingOrShortWindow()
    {
        Assert.Throws<ArgumentException>(() =>
            RemoteCommandService.ValidateChargingPlanRequest("VIN123", new ChargingPlanRequest
            {
                Enable = true,
                StartTime = 0
            }));
        Assert.Throws<ArgumentException>(() =>
            RemoteCommandService.ValidateChargingPlanRequest("VIN123", new ChargingPlanRequest
            {
                Enable = true,
                StartTime = 0,
                EndTime = 299_999
            }));
    }

    [Fact]
    public void ChargingPlanOwnershipRequiresMatchingIdAndWindow()
    {
        var tracked = new TrackedChargingPlan
        {
            PlanId = 42,
            PlanType = 0,
            StartTime = 1_000,
            EndTime = 301_000,
            Weeks = String.Empty
        };

        Assert.True(RemoteCommandService.ChargingPlanMatches(new ChargePlanItem
        {
            PlanId = 42,
            PlanType = "0",
            StartTime = 1_000,
            EndTime = 301_000,
            Weeks = String.Empty
        }, tracked));
        Assert.False(RemoteCommandService.ChargingPlanMatches(new ChargePlanItem
        {
            PlanId = 43,
            PlanType = "0",
            StartTime = 1_000,
            EndTime = 301_000,
            Weeks = String.Empty
        }, tracked));
        Assert.False(RemoteCommandService.ChargingPlanMatches(new ChargePlanItem
        {
            PlanId = 42,
            PlanType = "0",
            StartTime = 1_000,
            EndTime = 302_000,
            Weeks = String.Empty
        }, tracked));
    }
}
