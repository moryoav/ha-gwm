using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace libgwmapi;

/// <summary>
/// Adds the gwm-auth signature used by the current EU v2 authentication API.
/// </summary>
public sealed class GwmAuthSigningHandler : DelegatingHandler
{
    public const string AppKey = "1874226830";
    private const string AppSecret = "1eb6caa16ff203c96daf7f06309b8998";

    private readonly Func<string> _timestampProvider;
    private readonly Func<string> _nonceProvider;

    public GwmAuthSigningHandler()
        : this(
            () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            () => Guid.NewGuid().ToString("N")[..16])
    {
    }

    public GwmAuthSigningHandler(Func<string> timestampProvider, Func<string> nonceProvider)
    {
        _timestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
        _nonceProvider = nonceProvider ?? throw new ArgumentNullException(nameof(nonceProvider));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
        var method = request.Method.Method;
        var path = uri.AbsolutePath;
        var parameters = await BuildParametersAsync(request, method, uri, cancellationToken);
        var timestamp = _timestampProvider();
        var nonce = _nonceProvider();
        var auth = $"gwm-auth-appkey:{AppKey}" +
                   $"gwm-auth-nonce:{nonce}" +
                   $"gwm-auth-timestamp:{timestamp}";
        var raw = RemoveWhitespace(method + path + auth + parameters + AppSecret);
        var signature = Sha256Hex(Uri.EscapeDataString(raw));

        SetHeader(request, "gwm-auth-appkey", AppKey);
        SetHeader(request, "gwm-auth-nonce", nonce);
        SetHeader(request, "gwm-auth-timestamp", timestamp);
        SetHeader(request, "gwm-auth-sign", signature);

        return await base.SendAsync(request, cancellationToken);
    }

    private static async Task<string> BuildParametersAsync(
        HttpRequestMessage request,
        string method,
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (String.Equals(method, "POST", StringComparison.Ordinal))
        {
            if (request.Content is null)
            {
                return String.Empty;
            }

            await request.Content.LoadIntoBufferAsync();
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            return String.IsNullOrEmpty(body) ? String.Empty : "json=" + body;
        }

        var kept = new List<(string Key, string Value, string Token)>();
        var query = uri.Query.StartsWith('?') ? uri.Query[1..] : uri.Query;
        foreach (var token in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = token.IndexOf('=');
            var key = separator < 0 ? token : token[..separator];
            var value = separator < 0 ? String.Empty : token[(separator + 1)..];
            if (!String.IsNullOrEmpty(value))
            {
                kept.Add((key, value, $"{key}={value}"));
            }
        }

        kept.Sort((left, right) => StringComparer.Ordinal.Compare(left.Token, right.Token));
        var outgoingQuery = String.Join("&", kept.Select(item => item.Token));
        request.RequestUri = new Uri(
            uri.GetLeftPart(UriPartial.Path) +
            (kept.Count > 0 ? "?" + outgoingQuery : String.Empty));
        return String.Concat(kept.Select(item => $"{item.Key.ToLowerInvariant()}={item.Value}"));
    }

    private static void SetHeader(HttpRequestMessage request, string name, string value)
    {
        request.Headers.Remove(name);
        request.Headers.Add(name, value);
    }

    private static string RemoveWhitespace(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!Char.IsWhiteSpace(character))
            {
                result.Append(character);
            }
        }
        return result.ToString();
    }

    private static string Sha256Hex(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
