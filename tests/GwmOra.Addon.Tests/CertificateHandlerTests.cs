using System.Security.Cryptography.X509Certificates;
using libgwmapi;

namespace GwmOra.Addon.Tests;

public class CertificateHandlerTests
{
    [Fact]
    public void EuCertificateIsEuropeanGeneralClient()
    {
        var handler = new CertificateHandler("eu");
        using var cert = handler.CertificateWithPrivateKey;

        Assert.Contains("LGWGWM-AD-EU-GENERAL", cert.Subject);
        Assert.True(cert.HasPrivateKey);
    }

    [Fact]
    public void RusCertificateIsRussianGeneralClientFromApk()
    {
        var handler = new CertificateHandler("rus");
        using var cert = handler.CertificateWithPrivateKey;

        Assert.Contains("LGWGWM-AD-RU-GENERAL", cert.Subject);
        Assert.Contains("C=RU", cert.Subject.Replace(" ", String.Empty));
        Assert.True(cert.HasPrivateKey);
    }

    [Fact]
    public void RusChainIncludesRussianAppSubCa()
    {
        var handler = new CertificateHandler("rus");
        var subjects = handler.Chain.Cast<X509Certificate2>()
            .Select(c => c.Subject)
            .Where(s => !String.IsNullOrWhiteSpace(s))
            .ToArray();

        Assert.Contains(subjects, s => s.Contains("IOV APP General SubCA", StringComparison.Ordinal));
        Assert.Contains(subjects, s => s.Contains("IOV APP SubCA", StringComparison.Ordinal));
        Assert.Contains(
            subjects,
            s => s.Contains("IOV APP SubCA", StringComparison.Ordinal) &&
                 s.Contains("C=RU", StringComparison.Ordinal));
    }
}
