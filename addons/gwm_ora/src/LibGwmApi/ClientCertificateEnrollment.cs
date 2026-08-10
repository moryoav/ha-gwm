using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace libgwmapi;

public sealed record GeneratedClientCertificateRequest(string Csr, string PrivateKey);

public static class ClientCertificateEnrollment
{
    public static GeneratedClientCertificateRequest Generate(
        string country,
        string deviceId,
        DateTimeOffset? now = null)
    {
        var timestamp = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var normalizedCountry = (country ?? String.Empty).Trim().ToUpperInvariant();
        var normalizedDeviceId = NormalizeDeviceId(deviceId).ToUpperInvariant();
        var commonName = $"LGWMy GWM-AD-{normalizedCountry}{normalizedDeviceId}{timestamp}";

        using var rsa = RSA.Create(2048);
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCommonName(commonName);
        subject.AddOrganizationName("Great Wall Motor Co., Ltd.");
        subject.AddOrganizationalUnitName("EE System Design Dept");
        subject.AddStateOrProvinceName("Operational");

        var request = new CertificateRequest(
            subject.Build(),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return new GeneratedClientCertificateRequest(
            Convert.ToBase64String(request.CreateSigningRequest()),
            Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()));
    }

    public static X509Certificate2 LoadWithPrivateKey(
        string encodedCertificate,
        string encodedPrivateKey)
    {
        var certificate = X509CertificateLoader.LoadCertificate(
            Convert.FromBase64String(encodedCertificate));
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(encodedPrivateKey), out _);
        var withPrivateKey = certificate.CopyWithPrivateKey(rsa);
        certificate.Dispose();
        return withPrivateKey;
    }

    public static bool IsUsable(
        string? encodedCertificate,
        string? encodedPrivateKey,
        DateTimeOffset? now = null,
        TimeSpan? minimumRemainingLifetime = null)
    {
        if (String.IsNullOrWhiteSpace(encodedCertificate) ||
            String.IsNullOrWhiteSpace(encodedPrivateKey))
        {
            return false;
        }

        try
        {
            using var certificate = LoadWithPrivateKey(
                encodedCertificate,
                encodedPrivateKey);
            var instant = now ?? DateTimeOffset.UtcNow;
            var minimum = minimumRemainingLifetime ?? TimeSpan.FromDays(1);
            return certificate.HasPrivateKey &&
                   certificate.NotBefore.ToUniversalTime() <= instant.UtcDateTime.AddMinutes(5) &&
                   certificate.NotAfter.ToUniversalTime() > instant.UtcDateTime.Add(minimum);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string NormalizeDeviceId(string deviceId)
    {
        var normalized = (deviceId ?? String.Empty).Replace("-", String.Empty);
        return normalized.Length >= 32
            ? normalized[..32]
            : normalized.PadRight(32, '0');
    }
}
