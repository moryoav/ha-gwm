using GwmOra.Addon.Configuration;

namespace GwmOra.Addon.Gwm;

public sealed class VehiclePollingWorker : BackgroundService
{
    private readonly AddonOptions _options;
    private readonly GwmVehicleService _vehicleService;
    private readonly ILogger<VehiclePollingWorker> _logger;
    private string? _lastFailureKey;

    public VehiclePollingWorker(
        AddonOptions options,
        GwmVehicleService vehicleService,
        ILogger<VehiclePollingWorker> logger)
    {
        _options = options;
        _vehicleService = vehicleService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshIgnoringFailuresAsync(stoppingToken);

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

    private async Task RefreshIgnoringFailuresAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _vehicleService.RefreshNowAsync(cancellationToken);
            if (_lastFailureKey is not null)
            {
                _logger.LogInformation("GWM polling recovered");
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
            _logger.LogDebug("GWM poll still failing: {Message}", ex.Message);
            return;
        }

        _lastFailureKey = key;
        if (includeException)
        {
            _logger.LogWarning(ex, "GWM poll failed; the next poll will retry");
        }
        else
        {
            _logger.LogWarning("GWM poll is waiting for account verification: {Message}", ex.Message);
        }
    }
}
