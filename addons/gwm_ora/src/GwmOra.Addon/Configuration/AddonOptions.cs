using System.Text.Json;
using System.Text.Json.Serialization;

namespace GwmOra.Addon.Configuration;

public sealed class AddonOptions
{
    [JsonPropertyName("country")]
    public string Country { get; init; } = "DE";

    [JsonPropertyName("region")]
    public string Region { get; init; } = "eu";

    [JsonPropertyName("username")]
    public string Username { get; init; } = String.Empty;

    [JsonPropertyName("password")]
    public string Password { get; init; } = String.Empty;

    [JsonPropertyName("verification_code")]
    public string? VerificationCode { get; init; }

    [JsonPropertyName("security_pin")]
    public string? SecurityPin { get; init; }

    [JsonPropertyName("enable_remote_commands")]
    public bool EnableRemoteCommands { get; init; }

    [JsonPropertyName("poll_interval_seconds")]
    public int PollIntervalSeconds { get; init; } = 60;

    [JsonPropertyName("log_level")]
    public string LogLevel { get; init; } = "info";

    public Microsoft.Extensions.Logging.LogLevel ToMicrosoftLogLevel()
    {
        return LogLevel.Trim().ToLowerInvariant() switch
        {
            "trace" => Microsoft.Extensions.Logging.LogLevel.Trace,
            "debug" => Microsoft.Extensions.Logging.LogLevel.Debug,
            "info" => Microsoft.Extensions.Logging.LogLevel.Information,
            "warning" => Microsoft.Extensions.Logging.LogLevel.Warning,
            "error" => Microsoft.Extensions.Logging.LogLevel.Error,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };
    }
}

public static class AddonOptionsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static AddonOptions Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Add-on options file not found: {path}");
        }

        var options = JsonSerializer.Deserialize<AddonOptions>(File.ReadAllText(path), SerializerOptions)
                      ?? throw new InvalidOperationException($"Add-on options file is empty: {path}");

        return Validate(options);
    }

    private static AddonOptions Validate(AddonOptions options)
    {
        var country = (options.Country ?? String.Empty).Trim().ToUpperInvariant();
        if (country.Length != 2)
        {
            throw new InvalidOperationException("Option 'country' must be a two-letter country code such as DE or GB.");
        }

        var region = (options.Region ?? "eu").Trim().ToLowerInvariant();
        if (region is not ("eu" or "aus"))
        {
            throw new InvalidOperationException("Option 'region' must be 'eu' or 'aus'.");
        }

        if (String.IsNullOrWhiteSpace(options.Username))
        {
            throw new InvalidOperationException("Option 'username' is required.");
        }

        if (String.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException("Option 'password' is required.");
        }

        if (options.PollIntervalSeconds is < 30 or > 3600)
        {
            throw new InvalidOperationException("Option 'poll_interval_seconds' must be between 30 and 3600.");
        }

        var logLevel = (options.LogLevel ?? "info").Trim().ToLowerInvariant();
        if (logLevel is not ("trace" or "debug" or "info" or "warning" or "error"))
        {
            throw new InvalidOperationException("Option 'log_level' must be one of trace, debug, info, warning, or error.");
        }

        return new AddonOptions
        {
            Country = country,
            Region = region,
            Username = options.Username.Trim(),
            Password = options.Password,
            VerificationCode = String.IsNullOrWhiteSpace(options.VerificationCode) ? null : options.VerificationCode.Trim(),
            SecurityPin = String.IsNullOrWhiteSpace(options.SecurityPin) ? null : options.SecurityPin.Trim(),
            EnableRemoteCommands = options.EnableRemoteCommands,
            PollIntervalSeconds = options.PollIntervalSeconds,
            LogLevel = logLevel
        };
    }
}
