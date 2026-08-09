using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace libgwmapi;

/// <summary>
/// Adds the bt-auth signature used by the current European GWM app for
/// authenticated H5, certificate-enrollment, and vehicle API calls.
///
/// Unlike the AU/NZ signer, the EU app keeps empty query parameters in both
/// the outgoing URL and the signed payload. Query tokens are sorted in their
/// original encoded form, then names and values are URL-decoded for signing;
/// names are lowercased and key=value pairs are concatenated without separators.
/// </summary>
public sealed class EuBtAuthSigningHandler : DelegatingHandler
{
    public const string AppKey = "1874226830";
    private const string AppSecret = "1eb6caa16ff203c96daf7f06309b8998";

    private readonly Func<string> _timestampProvider;
    private readonly Func<string> _nonceProvider;

    public EuBtAuthSigningHandler()
        : this(
            () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            () => Guid.NewGuid().ToString("N")[..16].ToUpperInvariant())
    {
    }

    public EuBtAuthSigningHandler(Func<string> timestampProvider, Func<string> nonceProvider)
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
        var auth = $"bt-auth-appkey:{AppKey}" +
                   $"bt-auth-nonce:{nonce}" +
                   $"bt-auth-timestamp:{timestamp}";
        var raw = RemoveWhitespace(method + path + auth + parameters + AppSecret);
        var signature = Sha256Hex(Uri.EscapeDataString(raw));

        SetHeader(request, "bt-auth-appkey", AppKey);
        SetHeader(request, "bt-auth-nonce", nonce);
        SetHeader(request, "bt-auth-timestamp", timestamp);
        SetHeader(request, "bt-auth-sign", signature);

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

        var query = uri.Query.StartsWith('?') ? uri.Query[1..] : uri.Query;
        var tokens = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(token => token, StringComparer.Ordinal);

        var result = new StringBuilder();
        foreach (var token in tokens)
        {
            var separator = token.IndexOf('=');
            var encodedKey = separator < 0 ? token : token[..separator];
            var encodedValue = separator < 0 ? String.Empty : token[(separator + 1)..];
            var key = Uri.UnescapeDataString(encodedKey).ToLowerInvariant();
            var value = Uri.UnescapeDataString(encodedValue);
            result.Append(key).Append('=').Append(value);
        }

        return result.ToString();
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
