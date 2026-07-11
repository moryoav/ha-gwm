using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using GwmOra.Addon.Configuration;
using libgwmapi;

namespace GwmOra.Addon.Gwm;

public sealed class GwmApiClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public GwmApiClientFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public GwmApiClient Create(AddonOptions options, AddonState state)
    {
        var certHandler = new CertificateHandler();
        var httpHandler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual
        };

        using (var cert = certHandler.CertificateWithPrivateKey)
        {
            var pkcs12 = new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
            httpHandler.ClientCertificates.Add(pkcs12);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var store = new X509Store(StoreName.CertificateAuthority, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            foreach (var cert in certHandler.Chain)
            {
                if (cert.Issuer != cert.Subject)
                {
                    store.Add(cert);
                }
            }
        }

        var client = new GwmApiClient(new HttpClient(), new HttpClient(httpHandler), _loggerFactory, options.Region)
        {
            Country = options.Country
        };

        if (!String.IsNullOrWhiteSpace(state.AccessToken))
        {
            client.SetAccessToken(state.AccessToken);
        }

        return client;
    }
}
