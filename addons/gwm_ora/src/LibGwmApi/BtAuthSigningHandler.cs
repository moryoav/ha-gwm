using System.Security.Cryptography;
using System.Text;

namespace libgwmapi;

/// <summary>
/// Adds GWM's "bt-auth" request signature required by the AU/NZ (aus) gateway.
/// EU does not use this, so it is only attached for the aus region.
///
/// Signing (validated live against aus-h5-gateway, 2026-07-20):
///   ts     = unix milliseconds
///   nonce  = 16 hex chars
///   auth   = "bt-auth-appkey:{APP_KEY}bt-auth-nonce:{nonce}bt-auth-timestamp:{ts}"
///   params = POST : "json=" + rawBody
///            GET  : sorted NON-EMPTY "key=value" pairs joined by "&"  (empty params dropped)
///   raw    = METHOD + absolutePath + auth + params + APP_SEC          (all whitespace removed)
///   sign   = sha256_hex( urlencode(raw) )
/// For GET, empty-valued query params are also stripped from the outgoing URL (e.g.
/// "getLastStatus?vin=X&seqNo=" -> "getLastStatus?vin=X"); sending/​signing the empty
/// "seqNo=" is what the gateway rejects with 607099 "sign is inconformity".
/// </summary>
public sealed class BtAuthSigningHandler : DelegatingHandler
{
    public const string AppKey = "2186661209";
    private const string AppSec = "a9664fd3f97665e202e73880de03a0d8";
    private const string Prefix = "bt";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;              // "POST" / "GET"
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;                     // "/app-api/api/v1.0/..."

        string paramsPart;
        if (method == "POST")
        {
            var body = string.Empty;
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync();     // so the body can still be sent
                body = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            paramsPart = string.IsNullOrEmpty(body) ? string.Empty : "json=" + body;
        }
        else
        {
            // Keep only non-empty query params, sorted by key; drop empties from the URL too.
            var kept = new List<string>();
            var query = uri.Query.StartsWith('?') ? uri.Query[1..] : uri.Query;
            foreach (var token in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = token.IndexOf('=');
                var key = eq < 0 ? token : token[..eq];
                var val = eq < 0 ? string.Empty : token[(eq + 1)..];
                if (!string.IsNullOrEmpty(val))
                {
                    kept.Add($"{key}={val}");
                }
            }
            kept.Sort(StringComparer.Ordinal);
            paramsPart = string.Join("&", kept);
            var rebuilt = uri.GetLeftPart(UriPartial.Path) + (kept.Count > 0 ? "?" + paramsPart : string.Empty);
            if (rebuilt != uri.GetLeftPart(UriPartial.Query))
            {
                request.RequestUri = new Uri(rebuilt);
            }
        }

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var nonce = Guid.NewGuid().ToString("N")[..16];
        var auth = $"{Prefix}-auth-appkey:{AppKey}{Prefix}-auth-nonce:{nonce}{Prefix}-auth-timestamp:{ts}";
        var raw = StripWhitespace(method + path + auth + paramsPart + AppSec);
        var sign = Sha256Hex(Uri.EscapeDataString(raw));

        SetHeader(request, $"{Prefix}-auth-appkey", AppKey);
        SetHeader(request, $"{Prefix}-auth-nonce", nonce);
        SetHeader(request, $"{Prefix}-auth-timestamp", ts);
        SetHeader(request, $"{Prefix}-auth-sign", sign);

        return await base.SendAsync(request, cancellationToken);
    }

    private static void SetHeader(HttpRequestMessage request, string name, string value)
    {
        request.Headers.Remove(name);
        request.Headers.Add(name, value);
    }

    private static string StripWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (!char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string Sha256Hex(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}
