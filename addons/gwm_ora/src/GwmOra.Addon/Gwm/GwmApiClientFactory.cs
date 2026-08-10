using System.Net;
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
            var h5 = new HttpClient(new BtAuthSigningHandler
            {
                InnerHandler = CreatePlainHandler()
            });
            var app = new HttpClient(new BtAuthSigningHandler
            {
                InnerHandler = CreatePlainHandler()
            });

            client = new GwmApiClient(h5, app, _loggerFactory, "aus")
            {
                Country = options.Country,
                DeviceId = ApiDeviceId(state.DeviceId)
            };
        }
        else if (String.Equals(options.Region, "rus", StringComparison.OrdinalIgnoreCase))
        {
            // Russia (GWM.apk): mutual-TLS on the app gateway PLUS gwm-auth request signing on
            // every call (OverseasRequestHeaderInterceptor). Login hits rus-h5-gateway without a
            // client cert but still requires the signature — without it the gateway returns
            // "Значение подписи пустое".
            var certificateHandler = new CertificateHandler("rus");
            using var rusCertificate = certificateHandler.CertificateWithPrivateKey;
            var h5 = new HttpClient(new BtAuthSigningHandler(BtAuthSigningHandler.Profiles.Rus)
            {
                InnerHandler = CreatePlainHandler()
            });
            var app = new HttpClient(new BtAuthSigningHandler(BtAuthSigningHandler.Profiles.Rus)
            {
                InnerHandler = CreateTlsHandler(rusCertificate)
            });
            client = new GwmApiClient(h5, app, _loggerFactory, "rus")
            {
                Country = options.Country,
                DeviceId = state.DeviceId
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var store = new X509Store(
                    StoreName.CertificateAuthority,
                    StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);
                foreach (var certificate in certificateHandler.Chain)
                {
                    if (certificate.Issuer != certificate.Subject)
                    {
                        store.Add(certificate);
                    }
                }
            }
        }
        else
        {
            var certificateHandler = new CertificateHandler();
            using var generalCertificate = certificateHandler.CertificateWithPrivateKey;

            var h5 = new HttpClient(new EuBtAuthSigningHandler
            {
                InnerHandler = CreatePlainHandler()
            });
            var auth = new HttpClient(new GwmAuthSigningHandler
            {
                InnerHandler = CreatePlainHandler()
            });

            var commonTlsHandler = CreateTlsHandler(generalCertificate);
            var certificateClient = new HttpClient(new EuBtAuthSigningHandler
            {
                InnerHandler = commonTlsHandler
            });

            X509Certificate2? initialVehicleCertificate = null;
            if (ClientCertificateEnrollment.IsUsable(
                    state.ClientCertificate,
                    state.ClientPrivateKey))
            {
                initialVehicleCertificate = ClientCertificateEnrollment.LoadWithPrivateKey(
                    state.ClientCertificate!,
                    state.ClientPrivateKey!);
            }

            using (initialVehicleCertificate)
            {
                var appTlsHandler = CreateTlsHandler(
                    initialVehicleCertificate ?? generalCertificate);
                var app = new HttpClient(new EuBtAuthSigningHandler
                {
                    InnerHandler = appTlsHandler
                });

                client = new GwmApiClient(
                    h5,
                    auth,
                    app,
                    certificateClient,
                    _loggerFactory,
                    options.Region,
                    certificate => ReplaceClientCertificate(appTlsHandler, certificate))
                {
                    Country = options.Country,
                    DeviceId = ApiDeviceId(state.DeviceId)
                };
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var store = new X509Store(
                    StoreName.CertificateAuthority,
                    StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);
                foreach (var certificate in certificateHandler.Chain)
                {
                    if (certificate.Issuer != certificate.Subject)
                    {
                        store.Add(certificate);
                    }
                }
            }
        }

        if (!String.IsNullOrWhiteSpace(state.AccessToken))
        {
            client.SetAccessToken(state.AccessToken);
        }

        return client;
    }

    private static HttpClientHandler CreatePlainHandler()
    {
        return new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
    }

    private static HttpClientHandler CreateTlsHandler(X509Certificate2 certificate)
    {
        var handler = CreatePlainHandler();
        handler.ClientCertificateOptions = ClientCertificateOption.Manual;
        handler.ClientCertificates.Add(CloneCertificate(certificate));
        return handler;
    }

    private static void ReplaceClientCertificate(
        HttpClientHandler handler,
        X509Certificate2 certificate)
    {
        var existingCertificates = handler.ClientCertificates
            .Cast<X509Certificate2>()
            .ToArray();
        handler.ClientCertificates.Clear();
        foreach (var existing in existingCertificates)
        {
            existing.Dispose();
        }

        handler.ClientCertificates.Add(CloneCertificate(certificate));
    }

    private static X509Certificate2 CloneCertificate(X509Certificate2 certificate)
    {
        return X509CertificateLoader.LoadPkcs12(
            certificate.Export(X509ContentType.Pkcs12),
            null,
            X509KeyStorageFlags.EphemeralKeySet |
            X509KeyStorageFlags.Exportable);
    }

    internal static string ApiDeviceId(string deviceId)
    {
        var normalized = (deviceId ?? String.Empty).Replace("-", String.Empty);
        return normalized.Length >= 16
            ? normalized[..16]
            : normalized.PadRight(16, '0');
    }
}
