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
        GwmApiClient client;

        if (String.Equals(options.Region, "aus", StringComparison.OrdinalIgnoreCase))
        {
            // AU/NZ: no mutual-TLS client certificate; bt-auth request signing instead.
            var h5 = new HttpClient(new BtAuthSigningHandler { InnerHandler = new HttpClientHandler() });
            var app = new HttpClient(new BtAuthSigningHandler { InnerHandler = new HttpClientHandler() });
            client = new GwmApiClient(h5, app, _loggerFactory, "aus")
            {
                Country = options.Country,
                DeviceId = AusDeviceId(state.DeviceId)
            };
        }
        else
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

            client = new GwmApiClient(new HttpClient(), new HttpClient(httpHandler), _loggerFactory, options.Region)
            {
                Country = options.Country
            };
        }

        if (!String.IsNullOrWhiteSpace(state.AccessToken))
        {
            client.SetAccessToken(state.AccessToken);
        }

        return client;
    }

    // AU expects a 16-hex (iccid-like) device id; state.DeviceId is a 32-hex GUID, so take 16.
    private static string AusDeviceId(string deviceId)
    {
        var d = (deviceId ?? String.Empty).Replace("-", String.Empty);
        return d.Length >= 16 ? d.Substring(0, 16) : d.PadRight(16, '0');
    }
}
