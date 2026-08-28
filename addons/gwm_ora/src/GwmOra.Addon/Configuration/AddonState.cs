using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using libgwmapi.DTO.China;

namespace GwmOra.Addon.Configuration;

public sealed class AddonState
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = String.Empty;

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("gw_id")]
    public string? GwId { get; set; }

    [JsonPropertyName("bean_id")]
    public string? BeanId { get; set; }

    [JsonPropertyName("client_certificate")]
    public string? ClientCertificate { get; set; }

    [JsonPropertyName("client_private_key")]
    public string? ClientPrivateKey { get; set; }

    [JsonPropertyName("api_token")]
    public string ApiToken { get; set; } = String.Empty;

    [JsonPropertyName("discovery_uuid")]
    public string DiscoveryUuid { get; set; } = String.Empty;

    [JsonPropertyName("verification_code_requested_at")]
    public DateTimeOffset? VerificationCodeRequestedAt { get; set; }

    [JsonPropertyName("authentication_context_fingerprint")]
    public string? AuthenticationContextFingerprint { get; set; }

    [JsonPropertyName("china_session")]
    public ChinaSession? ChinaSession { get; set; }

    // Track the exact plan written for each vehicle. On a later configuration change this
    // lets the add-on distinguish its own leftover plan from one replaced in the GWM app.
    [JsonPropertyName("charging_plans_set_by_addon")]
    public Dictionary<string, TrackedChargingPlan> ChargingPlansSetByAddon { get; set; } =
        new(StringComparer.Ordinal);

    public bool EnsureGenerated()
    {
        var changed = false;
        if (String.IsNullOrWhiteSpace(DeviceId))
        {
            DeviceId = Guid.NewGuid().ToString("N");
            changed = true;
        }

        if (String.IsNullOrWhiteSpace(ApiToken))
        {
            ApiToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            changed = true;
        }

        if (String.IsNullOrWhiteSpace(DiscoveryUuid))
        {
            DiscoveryUuid = Guid.NewGuid().ToString("N");
            changed = true;
        }

        if (ChargingPlansSetByAddon is null)
        {
            ChargingPlansSetByAddon = new Dictionary<string, TrackedChargingPlan>(StringComparer.Ordinal);
            changed = true;
        }

        return changed;
    }
}

public sealed class TrackedChargingPlan
{
    [JsonPropertyName("plan_id")]
    public long? PlanId { get; set; }

    [JsonPropertyName("plan_type")]
    public int PlanType { get; set; }

    [JsonPropertyName("start_time")]
    public long StartTime { get; set; }

    [JsonPropertyName("end_time")]
    public long EndTime { get; set; }

    [JsonPropertyName("weeks")]
    public string Weeks { get; set; } = String.Empty;
}

public sealed class AddonStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    private AddonStateStore(string path, AddonState state)
    {
        _path = path;
        State = state;
    }

    public AddonState State { get; }

    public static AddonStateStore Load(string path)
    {
        AddonState state;
        if (File.Exists(path))
        {
            state = JsonSerializer.Deserialize<AddonState>(
                File.ReadAllText(path),
                SerializerOptions) ?? new AddonState();
        }
        else
        {
            state = new AddonState();
        }

        var store = new AddonStateStore(path, state);
        if (state.EnsureGenerated())
        {
            store.Save(CancellationToken.None);
        }

        return store;
    }

    public async Task UpdateAsync(
        Action<AddonState> update,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            update(State);
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TResult> ReadAsync<TResult>(
        Func<AddonState, TResult> read,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return read(State);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Save(CancellationToken cancellationToken)
    {
        _gate.Wait(cancellationToken);
        try
        {
            SaveCoreAsync(cancellationToken).GetAwaiter().GetResult();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!String.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                State,
                SerializerOptions,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, _path, true);
    }
}
