using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GwmOra.Addon.Configuration;

namespace GwmOra.Addon.Supervisor;

public sealed class SupervisorDiscoveryWorker : BackgroundService
{
    private const int ApiPort = 8099;
    private readonly AddonStateStore _stateStore;
    private readonly ILogger<SupervisorDiscoveryWorker> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public SupervisorDiscoveryWorker(AddonStateStore stateStore, ILogger<SupervisorDiscoveryWorker> logger)
    {
        _stateStore = stateStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");
        if (String.IsNullOrWhiteSpace(supervisorToken))
        {
            _logger.LogInformation("SUPERVISOR_TOKEN is not available; skipping Home Assistant Supervisor discovery");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            using var client = new HttpClient { BaseAddress = new Uri("http://supervisor/") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", supervisorToken);

            var selfInfo = await GetSelfInfoAsync(client, stoppingToken);
            var host = selfInfo.Hostname ?? "gwm_ora";
            var slug = selfInfo.Slug ?? "gwm_ora";

            var payload = new
            {
                service = "gwm_ora",
                config = new
                {
                    host,
                    port = ApiPort,
                    token = _stateStore.State.ApiToken,
                    api_version = 1,
                    slug,
                    uuid = _stateStore.State.DiscoveryUuid
                }
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("discovery", content, stoppingToken);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Published GWM add-on discovery for host {Host}", host);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish Home Assistant Supervisor discovery");
        }
    }

    private async Task<SelfInfo> GetSelfInfoAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("addons/self/info", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data))
        {
            return new SelfInfo(null, null);
        }

        var hostname = data.TryGetProperty("hostname", out var hostnameElement)
            ? hostnameElement.GetString()
            : null;
        var slug = data.TryGetProperty("slug", out var slugElement)
            ? slugElement.GetString()
            : null;
        return new SelfInfo(hostname, slug);
    }

    private sealed record SelfInfo(string? Hostname, string? Slug);
}
