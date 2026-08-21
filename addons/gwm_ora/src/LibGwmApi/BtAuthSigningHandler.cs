using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace libgwmapi;

/// <summary>
/// Adds GWM overseas request signatures required by the AU/NZ and Russia gateways.
/// EU does not use this; it relies on mutual TLS instead.
///
/// Validated against the official app family algorithm (AU live 2026-07-20; RU from GWM.apk
/// <c>OverseasRequestHeaderInterceptor</c>):
///   ts     = unix milliseconds
///   nonce  = 16 hex chars
///   auth   = "{prefix}-auth-appkey:{APP_KEY}{prefix}-auth-nonce:{nonce}{prefix}-auth-timestamp:{ts}"
///   params = POST : "json=" + rawBody
///            GET  : sorted NON-EMPTY "lowercase(key)=value" pairs concatenated without
///                   separators (empty params dropped)
///   raw    = METHOD + absolutePath + auth + params + APP_SEC          (all whitespace removed)
///   sign   = sha256_hex( urlencode(raw) )
/// For GET, empty-valued query params are also stripped from the outgoing URL (e.g.
/// "getLastStatus?vin=X&seqNo=" -> "getLastStatus?vin=X"); sending/​signing the empty
/// "seqNo=" is what the gateway rejects with 607099 "sign is inconformity".
///
/// Region credentials (from the official apps):
///   aus — prefix "bt",  appKey 2186661209, appSec a9664fd3…
///   rus — prefix "gwm", appKey 4694605273, appSec e4e478c0… (GWM.apk FeatureConfig / Env)
/// </summary>
public sealed class BtAuthSigningHandler : DelegatingHandler
{
    public readonly record struct Profile(string Prefix, string AppKey, string AppSec);

    public static class Profiles
    {
        public static readonly Profile Aus = new("bt", "2186661209", "a9664fd3f97665e202e73880de03a0d8");
        public static readonly Profile Rus = new("gwm", "4694605273", "e4e478c00f570e76a8993653a7b81d57");
    }

    /// <summary>AU/NZ app key (kept for callers that still reference it directly).</summary>
    public const string AppKey = "2186661209";

    private readonly Profile _profile;
    private readonly Func<string> _timestampProvider;
    private readonly Func<string> _nonceProvider;

    public BtAuthSigningHandler()
        : this(Profiles.Aus)
    {
    }

    public BtAuthSigningHandler(Profile profile)
        : this(
            profile,
            () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            () => Guid.NewGuid().ToString("N")[..16])
    {
    }

    internal BtAuthSigningHandler(Func<string> timestampProvider, Func<string> nonceProvider)
        : this(Profiles.Aus, timestampProvider, nonceProvider)
    {
    }

    internal BtAuthSigningHandler(Profile profile, Func<string> timestampProvider, Func<string> nonceProvider)
    {
        _profile = profile;
        _timestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
        _nonceProvider = nonceProvider ?? throw new ArgumentNullException(nameof(nonceProvider));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;              // "POST" / "GET"
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;                     // "/app-api/api/v1.0/..."
        var prefix = _profile.Prefix;

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
            // Keep only non-empty query params and sort their original tokens. The signed
            // payload lowercases each key and concatenates pairs without separators; the
            // outgoing URL remains a conventional ampersand-delimited query string.
            var kept = new List<(string Key, string Value, string Token)>();
            var query = uri.Query.StartsWith('?') ? uri.Query[1..] : uri.Query;
            foreach (var token in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = token.IndexOf('=');
                var key = eq < 0 ? token : token[..eq];
                var val = eq < 0 ? string.Empty : token[(eq + 1)..];
                if (!string.IsNullOrEmpty(val))
                {
                    kept.Add((key, val, $"{key}={val}"));
                }
            }
            kept.Sort((left, right) => StringComparer.Ordinal.Compare(left.Token, right.Token));
            paramsPart = string.Concat(kept.Select(item => $"{item.Key.ToLowerInvariant()}={item.Value}"));
            var outgoingQuery = string.Join("&", kept.Select(item => item.Token));
            var rebuilt = uri.GetLeftPart(UriPartial.Path) +
                          (kept.Count > 0 ? "?" + outgoingQuery : string.Empty);
            if (rebuilt != uri.GetLeftPart(UriPartial.Query))
            {
                request.RequestUri = new Uri(rebuilt);
            }
        }

        var ts = _timestampProvider();
        var nonce = _nonceProvider();
        var auth = $"{prefix}-auth-appkey:{_profile.AppKey}{prefix}-auth-nonce:{nonce}{prefix}-auth-timestamp:{ts}";
        var raw = StripWhitespace(method + path + auth + paramsPart + _profile.AppSec);
        var sign = Sha256Hex(Uri.EscapeDataString(raw));

        SetHeader(request, $"{prefix}-auth-appkey", _profile.AppKey);
        SetHeader(request, $"{prefix}-auth-nonce", nonce);
        SetHeader(request, $"{prefix}-auth-timestamp", ts);
        SetHeader(request, $"{prefix}-auth-sign", sign);

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
