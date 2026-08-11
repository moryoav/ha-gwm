using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using libgwmapi;

namespace GwmOra.Addon.Tests;

public class ClientCertificateEnrollmentTests
{
    [Fact]
    public void GenerateCreatesRsa2048PrivateKeyAndDerCsr()
    {
        var generated = ClientCertificateEnrollment.Generate(
            "IL",
            "0123456789abcdef0123456789abcdef",
            DateTimeOffset.FromUnixTimeSeconds(1786119079));

        Assert.NotEmpty(Convert.FromBase64String(generated.Csr));

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(generated.PrivateKey), out _);
        Assert.Equal(2048, rsa.KeySize);
    }

    [Fact]
    public void LoadWithPrivateKeyAttachesMatchingPrivateKey()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=GWM test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var original = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(2));

        var encodedCertificate = Convert.ToBase64String(
            original.Export(X509ContentType.Cert));
        var encodedPrivateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());

        using var loaded = ClientCertificateEnrollment.LoadWithPrivateKey(
            encodedCertificate,
            encodedPrivateKey);

        Assert.True(loaded.HasPrivateKey);
        Assert.Equal(original.Thumbprint, loaded.Thumbprint);
        Assert.True(ClientCertificateEnrollment.IsUsable(
            encodedCertificate,
            encodedPrivateKey));
    }
}
