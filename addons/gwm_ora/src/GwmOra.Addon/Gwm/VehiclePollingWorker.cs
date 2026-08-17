using System.Linq;
using GwmOra.Addon.Configuration;
using GwmOra.Addon.RemoteCommands;

namespace GwmOra.Addon.Gwm;

public sealed class VehiclePollingWorker : BackgroundService
{
    private readonly AddonOptions _options;
    private readonly GwmVehicleService _vehicleService;
    private readonly RemoteCommandService _commands;
    private readonly ILogger<VehiclePollingWorker> _logger;
    private string? _lastFailureKey;

    public VehiclePollingWorker(
        AddonOptions options,
        GwmVehicleService vehicleService,
        RemoteCommandService commands,
        ILogger<VehiclePollingWorker> logger)
    {
        _options = options;
        _vehicleService = vehicleService;
        _commands = commands;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshIgnoringFailuresAsync(stoppingToken);
        await ClearLeftoverChargingPlanAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RefreshIgnoringFailuresAsync(stoppingToken);
        }
    }

    private async Task ClearLeftoverChargingPlanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var vins = _vehicleService.GetVehicles().Vehicles
                .Select(v => v.Vin)
                .Where(v => !String.IsNullOrWhiteSpace(v));
            await _commands.ClearAddonChargingPlanIfDisabledAsync(vins, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Charging-plan cleanup skipped");
        }
    }

    private async Task RefreshIgnoringFailuresAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _vehicleService.RefreshNowAsync(cancellationToken);
            if (_lastFailureKey is not null)
            {
                _logger.LogInformation("GWM ORA polling recovered");
                _lastFailureKey = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (GwmVerificationRequiredException ex)
        {
            LogFailure(ex, includeException: false);
        }
        catch (Exception ex)
        {
            LogFailure(ex, includeException: true);
        }
    }

    private void LogFailure(Exception ex, bool includeException)
    {
        var key = $"{ex.GetType().FullName}:{ex.Message}";
        if (String.Equals(key, _lastFailureKey, StringComparison.Ordinal))
        {
            _logger.LogDebug("GWM ORA poll still failing: {Message}", ex.Message);
            return;
        }

        _lastFailureKey = key;
        if (includeException)
        {
            _logger.LogWarning(ex, "GWM ORA poll failed; the next poll will retry");
        }
        else
        {
            _logger.LogWarning("GWM ORA poll is waiting for account verification: {Message}", ex.Message);
        }
    }
}
