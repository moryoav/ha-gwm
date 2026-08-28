#nullable enable

using System.Security.Cryptography;
using System.Text;

namespace libgwmapi.China;

internal static class ChinaCrypto
{
    internal const string DefaultNoteId = "145765423214576567716671";
    internal const string DefaultSecret32 = "E3*138%pb=GcflmhmsaA4WU^J-f&0Ofe";
    internal const string DefaultSecret36 = "t8X_MybKFjp-Kg^mt99ALe-ArGzJE5mpCOra";
    internal const string BeanTechAppKey = "7863128529";
    internal const string BeanTechSecret = "21382b32fea1d5fa03813d806d2dd64f";
    internal const string AutoAiCKey = "ea49a50f914b8d38af1c84809d302683";
    internal const string AutoAiPrivateKey = "dad377585f566b548c961a418dcec41a";

    private const string GAppPassword1 =
        "Qin.1^0123456789abcdef0123456789abcdef0123456789abcdef012345cdef";
    private const string GAppPassword2 =
        "Gwn*9$0123456789abcdef0123456789abcdef0189abcdef0123456789abcdef";

    internal static string EncryptGApp(string json, int keyId = 1)
    {
        var password = keyId switch
        {
            1 => GAppPassword1,
            2 => GAppPassword2,
            _ => throw new ArgumentOutOfRangeException(nameof(keyId))
        };
        var salt = RandomNumberGenerator.GetBytes(8);
        var (key, iv) = DeriveOpenSslKey(password, salt);
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        var plaintext = Encoding.UTF8.GetBytes(json);
        var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        var salted = new byte[16 + ciphertext.Length];
        Encoding.ASCII.GetBytes("Salted__").CopyTo(salted, 0);
        salt.CopyTo(salted, 8);
        ciphertext.CopyTo(salted, 16);
        return $"G_A({Convert.ToBase64String(salted)},{keyId})";
    }

    internal static string DecryptGApp(string wrapped)
    {
        if (!wrapped.StartsWith("G_A(", StringComparison.Ordinal)
            || !wrapped.EndsWith(')'))
        {
            return wrapped;
        }

        var separator = wrapped.LastIndexOf(',');
        if (separator <= 4
            || !Int32.TryParse(wrapped.AsSpan(separator + 1, wrapped.Length - separator - 2), out var keyId))
        {
            throw new CryptographicException("Invalid China G_A response wrapper.");
        }

        var password = keyId switch
        {
            1 => GAppPassword1,
            2 => GAppPassword2,
            _ => throw new CryptographicException($"Unsupported China G_A key id {keyId}.")
        };
        var encrypted = Convert.FromBase64String(wrapped[4..separator]);
        if (encrypted.Length < 32
            || !encrypted.AsSpan(0, 8).SequenceEqual(Encoding.ASCII.GetBytes("Salted__")))
        {
            throw new CryptographicException("Invalid China G_A encrypted payload.");
        }

        var salt = encrypted.AsSpan(8, 8).ToArray();
        var (key, iv) = DeriveOpenSslKey(password, salt);
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plaintext = decryptor.TransformFinalBlock(encrypted, 16, encrypted.Length - 16);
        return Encoding.UTF8.GetString(plaintext);
    }

    internal static string DefaultSign(
        string method,
        string signingUrl,
        string? rawBody,
        IReadOnlyDictionary<string, string> headers)
    {
        var timestamp = Header(headers, "Timestamp");
        var authorization = Header(headers, "Authorization");
        var deviceId = Header(headers, "DeviceId");
        var canonical = new StringBuilder(method.ToUpperInvariant())
            .Append(signingUrl);
        foreach (var name in new[]
                 {
                     "AppId", "Authorization", "DeviceId", "NoteId",
                     "SourceApp", "SourceAppVer", "SourceType", "Timestamp"
                 })
        {
            canonical.Append(name.ToLowerInvariant())
                .Append(':')
                .Append(Header(headers, name));
        }

        if (!HttpMethod.Get.Method.Equals(method, StringComparison.OrdinalIgnoreCase))
        {
            canonical.Append("json=").Append(rawBody ?? String.Empty);
        }

        canonical.Append(DefaultDerivedSecret(timestamp, deviceId, authorization));
        return Sha256Hex(canonical.ToString());
    }

    internal static string BeanTechSign(
        string method,
        string path,
        string nonce,
        string timestamp,
        string parameter)
    {
        var decodedPath = "/" + String.Join(
            '/',
            path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString));
        var auth = $"bt-auth-appkey:{BeanTechAppKey}" +
                   $"bt-auth-nonce:{nonce}" +
                   $"bt-auth-timestamp:{timestamp}";
        var encoded = JavaUrlEncode(
            method.ToUpperInvariant() + decodedPath + auth + parameter + BeanTechSecret);
        foreach (var whitespace in new[] { "+", "%20", "%0A", "%09", "%0D" })
        {
            encoded = encoded.Replace(whitespace, String.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return Sha256Hex(encoded);
    }

    internal static string AutoAiSign(string timestamp)
    {
        var key = Encoding.UTF8.GetBytes($"C_KEY={AutoAiCKey}&API_KEY={AutoAiPrivateKey}");
        var message = Encoding.UTF8.GetBytes($"SIGN_BODY=[]&SIGN_TIME={timestamp}");
        using var hmac = new HMACSHA1(key);
        return Convert.ToBase64String(hmac.ComputeHash(message));
    }

    internal static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static string Md5Hex(string value) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string JavaUrlEncode(string value)
    {
        var result = new StringBuilder();
        foreach (var octet in Encoding.UTF8.GetBytes(value))
        {
            var character = (char)octet;
            if (character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '_' or '.' or '*')
            {
                result.Append(character);
            }
            else if (character == ' ')
            {
                result.Append('+');
            }
            else
            {
                result.Append('%').Append(octet.ToString("X2"));
            }
        }

        return result.ToString();
    }

    private static string DefaultDerivedSecret(
        string timestampText,
        string deviceId,
        string authorization)
    {
        if (!Int64.TryParse(timestampText, out var timestamp))
        {
            timestamp = 0;
        }

        var index = (int)(timestamp % 100000 % 32);
        var source = DefaultSecret36;
        if ((timestamp & 1) == 1)
        {
            var transposed = new StringBuilder(36);
            for (var column = 0; column < 6; column++)
            {
                for (var row = 0; row < 6; row++)
                {
                    transposed.Append(DefaultSecret36[(row * 6) + column]);
                }
            }
            source = transposed.ToString();
        }

        var repeated = source + source;
        var secretSelection = repeated.Substring(index, 6);
        var deviceOffset = (int)(timestamp % 8);
        var deviceSelection = deviceId.Length >= deviceOffset + 6
            ? deviceId.Substring(deviceOffset, 6)
            : String.Empty;
        var trimmedAuthorization = authorization.Trim();
        var authSelection = trimmedAuthorization.Length > 9
            ? trimmedAuthorization.Substring(3, 6)
            : String.Empty;
        return DefaultSecret32 + secretSelection + deviceSelection + authSelection;
    }

    private static string Header(IReadOnlyDictionary<string, string> headers, string name) =>
        headers.TryGetValue(name, out var value) ? value ?? String.Empty : String.Empty;

    private static (byte[] Key, byte[] Iv) DeriveOpenSslKey(string password, byte[] salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var derived = new List<byte>(48);
        var previous = Array.Empty<byte>();
        while (derived.Count < 48)
        {
            var input = new byte[previous.Length + passwordBytes.Length + salt.Length];
            previous.CopyTo(input, 0);
            passwordBytes.CopyTo(input, previous.Length);
            salt.CopyTo(input, previous.Length + passwordBytes.Length);
            previous = MD5.HashData(input);
            derived.AddRange(previous);
        }

        return (derived.Take(32).ToArray(), derived.Skip(32).Take(16).ToArray());
    }
}
