using GwmOra.Addon.Configuration;
using GwmOra.Addon.Supervisor;
using libgwmapi;
using libgwmapi.DTO.AppAuth;
using libgwmapi.DTO.UserAuth;

namespace GwmOra.Addon.Gwm;

public sealed class GwmAuthenticationService
{
    private static readonly TimeSpan VerificationCodeRequestInterval =
        TimeSpan.FromMinutes(10);
    private static readonly int[] RusAgreements = { 1, 2, 18, 19 };

    private readonly AddonOptions _options;
    private readonly AddonStateStore _stateStore;
    private readonly SupervisorOptionsService _supervisorOptions;
    private readonly ILogger<GwmAuthenticationService> _logger;

    public GwmAuthenticationService(
        AddonOptions options,
        AddonStateStore stateStore,
        SupervisorOptionsService supervisorOptions,
        ILogger<GwmAuthenticationService> logger)
    {
        _options = options;
        _stateStore = stateStore;
        _supervisorOptions = supervisorOptions;
        _logger = logger;
    }

    public async Task EnsureAuthenticatedAsync(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        if (IsChinaRegion())
        {
            await EnsureChinaAuthenticatedAsync(client, cancellationToken);
            return;
        }

        var isAus = String.Equals(
            _options.Region,
            "aus",
            StringComparison.OrdinalIgnoreCase);
        var requiresPerDeviceCertificate = RequiresPerDeviceClientCertificate();

        if (!String.IsNullOrWhiteSpace(_stateStore.State.AccessToken))
        {
            client.SetAccessToken(_stateStore.State.AccessToken);
            var storedTokenAccepted = false;
            try
            {
                var user = await client.GetUserBaseInfoAsync(cancellationToken);
                if (!isAus)
                {
                    await StoreUserIdentityAsync(
                        user.GwId,
                        user.BeanId,
                        cancellationToken);
                }
                storedTokenAccepted = true;
            }
            catch (GwmApiException ex)
            {
                _logger.LogInformation(
                    "Stored GWM access token rejected: {Code} {Message}",
                    ex.Code,
                    ex.Message);

                if (isAus && ex.Code == "607501")
                {
                    await LoginAsync(client, cancellationToken);
                    return;
                }
            }

            if (storedTokenAccepted)
            {
                if (requiresPerDeviceCertificate)
                {
                    await EnsureEuClientCertificateAsync(client, cancellationToken);
                }
                return;
            }
        }

        if (!String.IsNullOrWhiteSpace(_stateStore.State.RefreshToken))
        {
            var refreshed = false;
            try
            {
                await RefreshTokenAsync(client, cancellationToken);
                var user = await client.GetUserBaseInfoAsync(cancellationToken);
                if (!isAus)
                {
                    await StoreUserIdentityAsync(
                        user.GwId,
                        user.BeanId,
                        cancellationToken);
                }
                refreshed = true;
            }
            catch (GwmApiException ex)
            {
                _logger.LogWarning(
                    "GWM token refresh failed: {Code} {Message}",
                    ex.Code,
                    ex.Message);
                if (isAus && ex.Code == "607501")
                {
                    await LoginAsync(client, cancellationToken);
                    return;
                }
            }

            if (refreshed)
            {
                if (requiresPerDeviceCertificate)
                {
                    await EnsureEuClientCertificateAsync(client, cancellationToken);
                }
                return;
            }
        }

        await LoginAsync(client, cancellationToken);
        if (requiresPerDeviceCertificate)
        {
            await EnsureEuClientCertificateAsync(client, cancellationToken);
        }
    }

    private async Task EnsureChinaAuthenticatedAsync(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        client.SetChinaSession(_stateStore.State.ChinaSession);
        var storedSession = _stateStore.State.ChinaSession;
        if (storedSession?.IsComplete == true)
        {
            try
            {
                await client.ValidateChinaSessionAsync(cancellationToken);
                return;
            }
            catch (GwmApiException ex)
            {
                _logger.LogInformation(
                    "Stored experimental GWM China session was rejected: {Code} {Message}",
                    ex.Code,
                    ex.Message);
            }
        }

        if (storedSession?.HasGAppTokens == true)
        {
            try
            {
                var refreshed = await client.RefreshChinaSessionAsync(cancellationToken);
                await StoreChinaSessionAsync(refreshed, cancellationToken);
                await client.ValidateChinaSessionAsync(cancellationToken);
                return;
            }
            catch (GwmApiException ex)
            {
                _logger.LogWarning(
                    "Experimental GWM China session refresh failed: {Code} {Message}",
                    ex.Code,
                    ex.Message);
            }
        }

        if (String.IsNullOrWhiteSpace(_options.VerificationCode))
        {
            await RequestVerificationCodeChinaAsync(client, cancellationToken);
            throw new GwmVerificationRequiredException(
                "GWM China uses SMS sign-in. A verification code was requested for the configured " +
                "phone number; enter it in 'verification_code', save, and restart the add-on.");
        }

        try
        {
            var session = await client.LoginChinaWithSmsAsync(
                _options.Username,
                _options.VerificationCode.Trim(),
                cancellationToken);
            await StoreChinaSessionAsync(session, cancellationToken);
            await _supervisorOptions.ClearVerificationCodeAsync(cancellationToken);
            _logger.LogInformation(
                "Experimental GWM China SMS code accepted and vehicle-service tokens stored");
        }
        catch (GwmApiException ex)
        {
            if (ex.Code == "CN_TSP_LOGIN")
            {
                var partialSession = client.GetChinaSession();
                if (partialSession.HasGAppTokens)
                {
                    await StoreChinaSessionAsync(partialSession, cancellationToken);
                    await _supervisorOptions.ClearVerificationCodeAsync(cancellationToken);
                }

                throw new GwmVerificationRequiredException(
                    "GWM China accepted the SMS code, but initialization of the vehicle services failed. " +
                    "The partial session was saved, so restart the add-on once to retry without requesting " +
                    $"another code. If it repeats, report this response: {ex.Message} [{ex.Code}]",
                    ex);
            }

            await _stateStore.UpdateAsync(
                state => state.VerificationCodeRequestedAt = null,
                cancellationToken);
            throw new GwmVerificationRequiredException(
                "GWM China rejected the configured verification_code. Clear it, restart the add-on " +
                "to request a fresh SMS code, then enter the new code and restart again. " +
                $"GWM response: {ex.Message} [{ex.Code}]",
                ex);
        }
    }

    private async Task RequestVerificationCodeChinaAsync(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var lastRequest = _stateStore.State.VerificationCodeRequestedAt;
        if (lastRequest.HasValue && now - lastRequest.Value < VerificationCodeRequestInterval)
        {
            return;
        }

        try
        {
            await client.RequestChinaSmsCodeAsync(_options.Username, cancellationToken);
            await _stateStore.UpdateAsync(
                state => state.VerificationCodeRequestedAt = now,
                cancellationToken);
            _logger.LogWarning(
                "Experimental GWM China login: an SMS code was sent to the configured phone number");
        }
        catch (GwmApiException ex)
        {
            throw new GwmVerificationRequiredException(
                $"GWM China requires SMS verification, but requesting a code failed: " +
                $"{ex.Message} [{ex.Code}]",
                ex);
        }
    }

    private Task StoreChinaSessionAsync(
        libgwmapi.DTO.China.ChinaSession session,
        CancellationToken cancellationToken) =>
        _stateStore.UpdateAsync(state =>
        {
            state.ChinaSession = session.Clone();
            state.VerificationCodeRequestedAt = null;
            // Prevent an old overseas token from looking authoritative if the user
            // changes the same add-on installation to the China region.
            state.AccessToken = null;
            state.RefreshToken = null;
        }, cancellationToken);

    private bool IsChinaRegion() =>
        String.Equals(_options.Region, "cn", StringComparison.OrdinalIgnoreCase);

    private async Task RefreshTokenAsync(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        var isAus = String.Equals(
            _options.Region,
            "aus",
            StringComparison.OrdinalIgnoreCase);
        var request = new RefreshTokenRequest
        {
            DeviceId = client.DeviceId,
            AccessToken = _stateStore.State.AccessToken,
            RefreshToken = _stateStore.State.RefreshToken
        };

        if (!isAus)
        {
            client.SetAccessToken(String.Empty);
        }

        var response = await client.RefreshTokenAsync(request, cancellationToken);
        await _stateStore.UpdateAsync(state =>
        {
            state.AccessToken = response.AccessToken;
            state.RefreshToken = response.RefreshToken;
        }, cancellationToken);
        client.SetAccessToken(response.AccessToken);
    }

    private async Task LoginAsync(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        client.SetAccessToken(String.Empty);

        if (String.Equals(
                _options.Region,
                "aus",
                StringComparison.OrdinalIgnoreCase))
        {
            await LoginAusAsync(client, cancellationToken);
            return;
        }

        if (String.Equals(
                _options.Region,
                "rus",
                StringComparison.OrdinalIgnoreCase))
        {
            await LoginRusAsync(client, cancellationToken);
            return;
        }

        await LoginEuV2Async(client, cancellationToken);
    }

    private async Task LoginEuV2Async(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        var countryCallingCode = CountryCallingCodeResolver.Resolve(_options.Country);
        var request = new EuLoginWithPasswordRequest
        {
            Account = _options.Username,
            CountryCode = countryCallingCode,
            Password = _options.Password,
            DeviceId = client.DeviceId,
            Country = _options.Country
        };

        if (!String.IsNullOrWhiteSpace(_options.VerificationCode))
        {
            var verificationCode = _options.VerificationCode.Trim();
            try
            {
                await client.CheckVerifyCodeV2Async(new EuCheckVerifyCodeRequest
                {
                    Account = _options.Username,
                    VerifyCode = verificationCode,
                    CountryCode = countryCallingCode
                }, cancellationToken);

                request.VerifyCode = verificationCode;
                request.ValidCodeMode = "1";
                var verifiedResponse = await client.LoginWithPasswordV2Async(
                    request,
                    cancellationToken);
                await StoreLoginResponseAsync(
                    client,
                    verifiedResponse,
                    cancellationToken);
                await _supervisorOptions.ClearVerificationCodeAsync(cancellationToken);
                _logger.LogInformation(
                    "GWM v2 verification code accepted and tokens stored");
                return;
            }
            catch (GwmApiException ex)
            {
                await _stateStore.UpdateAsync(
                    state => state.VerificationCodeRequestedAt = null,
                    cancellationToken);
                throw new GwmVerificationRequiredException(
                    "GWM rejected the configured verification_code. Clear it, restart the " +
                    "add-on to request a fresh code, then enter the new code and restart again.",
                    ex);
            }
        }

        try
        {
            var response = await client.LoginWithPasswordV2Async(
                request,
                cancellationToken);
            await StoreLoginResponseAsync(client, response, cancellationToken);
        }
        catch (GwmApiException ex) when (IsEuVerificationRequired(ex))
        {
            await RequestVerificationCodeEuV2Async(
                client,
                countryCallingCode,
                cancellationToken);
            throw new GwmVerificationRequiredException(
                "GWM requires e-mail/SMS verification. A verification code was requested; " +
                "enter it in the add-on option 'verification_code', save, and restart the add-on.",
                ex);
        }
    }

    // Russia (rus-h5-gateway): loginAccount with plaintext password and NO countryCode.
    // Live probe (2026-08-10): sending countryCode="RU" forces phone validation
    // ("Телефон в 8-32 номерах") and rejects e-mail accounts; omitting countryCode
    // lets e-mail + plaintext password succeed. Password is validated as 8-32 chars
    // with upper/lower/digits (308100), so MD5/RSA ciphertext must not be used.
    private async Task LoginRusAsync(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        if (!String.IsNullOrWhiteSpace(_options.VerificationCode))
        {
            await LoginWithVerificationCodeRusAsync(client, cancellationToken);
            return;
        }

        var request = CreateRussianPasswordLoginRequest(_options, client.DeviceId);

        try
        {
            var response = await client.LoginAccountAsync(request, cancellationToken);
            await StoreLoginResponseAsync(client, response, cancellationToken);
        }
        catch (GwmApiException ex) when (IsRusVerificationRequired(ex))
        {
            await RequestVerificationCodeRusAsync(client, cancellationToken);
            throw new GwmVerificationRequiredException(
                "GWM requires SMS/e-mail verification. A verification code was requested; " +
                "enter it in the add-on option 'verification_code', save, and restart the add-on.",
                ex);
        }
    }

    private async Task LoginWithVerificationCodeRusAsync(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        var request = CreateRussianVerificationLoginRequest(_options, client.DeviceId);

        try
        {
            var response = await client.LoginWithSmsAsync(request, cancellationToken);
            await StoreLoginResponseAsync(client, response, cancellationToken);
            await _supervisorOptions.ClearVerificationCodeAsync(cancellationToken);
            _logger.LogInformation("GWM (RU) verification code accepted and tokens stored");
        }
        catch (GwmApiException ex)
        {
            await _stateStore.UpdateAsync(
                state => state.VerificationCodeRequestedAt = null,
                cancellationToken);
            throw new GwmVerificationRequiredException(
                "GWM rejected the configured verification_code. Clear it, restart the add-on " +
                "to request a fresh code, then enter the new code and restart again.",
                ex);
        }
    }

    private async Task RequestVerificationCodeRusAsync(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var lastRequest = _stateStore.State.VerificationCodeRequestedAt;
        if (lastRequest.HasValue &&
            now - lastRequest.Value < VerificationCodeRequestInterval)
        {
            return;
        }

        try
        {
            await client.GetSmsCodeAsync(
                new GetSmsCode { Email = _options.Username },
                cancellationToken);
            await _stateStore.UpdateAsync(
                state => state.VerificationCodeRequestedAt = now,
                cancellationToken);
            _logger.LogWarning(
                "GWM (RU) requested account verification; a verification code was sent to the " +
                "account e-mail/SMS channel");
        }
        catch (GwmApiException ex)
        {
            throw new GwmVerificationRequiredException(
                $"GWM requires SMS/e-mail verification, but requesting a verification code failed: {ex.Message}",
                ex);
        }
    }

    internal static LoginAccountRequest CreateRussianPasswordLoginRequest(
        AddonOptions options,
        string deviceId)
    {
        return new LoginAccountRequest
        {
            Country = options.Country,
            IsEncrypt = false,
            DeviceId = deviceId,
            Model = "ha-gwm-ora",
            PushToken = String.Empty,
            Account = options.Username,
            Password = options.Password,
            Agreement = RusAgreements
        };
    }

    internal static LoginWithSmsRequest CreateRussianVerificationLoginRequest(
        AddonOptions options,
        string deviceId)
    {
        if (String.IsNullOrWhiteSpace(options.VerificationCode))
        {
            throw new InvalidOperationException(
                "A verification code is required for Russian verification login.");
        }

        return new LoginWithSmsRequest
        {
            Agreement = RusAgreements,
            Email = options.Username,
            Country = options.Country,
            DeviceId = deviceId,
            Model = "ha-gwm-ora",
            PushToken = String.Empty,
            SmsCode = options.VerificationCode.Trim()
        };
    }

    private async Task RequestVerificationCodeEuV2Async(
        GwmApiClient client,
        string countryCallingCode,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var lastRequest = _stateStore.State.VerificationCodeRequestedAt;
        if (lastRequest.HasValue &&
            now - lastRequest.Value < VerificationCodeRequestInterval)
        {
            return;
        }

        try
        {
            await client.GetVerifyCodeV2Async(new EuGetVerifyCodeRequest
            {
                Account = _options.Username,
                CountryCode = countryCallingCode
            }, cancellationToken);
            await _stateStore.UpdateAsync(
                state => state.VerificationCodeRequestedAt = now,
                cancellationToken);
            _logger.LogWarning(
                "GWM v2 requested account verification; a code was sent to the " +
                "account e-mail/SMS channel");
        }
        catch (GwmApiException ex)
        {
            throw new GwmVerificationRequiredException(
                $"GWM requires verification, but requesting a verification code failed: {ex.Message}",
                ex);
        }
    }

    private async Task EnsureEuClientCertificateAsync(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        if (ClientCertificateEnrollment.IsUsable(
                _stateStore.State.ClientCertificate,
                _stateStore.State.ClientPrivateKey))
        {
            using var storedCertificate =
                ClientCertificateEnrollment.LoadWithPrivateKey(
                    _stateStore.State.ClientCertificate!,
                    _stateStore.State.ClientPrivateKey!);
            client.SetVehicleClientCertificate(storedCertificate);
            return;
        }

        if (String.IsNullOrWhiteSpace(_stateStore.State.GwId))
        {
            var user = await client.GetUserBaseInfoAsync(cancellationToken);
            await StoreUserIdentityAsync(
                user.GwId,
                user.BeanId,
                cancellationToken);
        }

        var gwId = _stateStore.State.GwId;
        if (String.IsNullOrWhiteSpace(gwId))
        {
            throw new InvalidOperationException(
                "GWM login succeeded, but no gwId was returned for client-certificate enrollment.");
        }

        var now = DateTimeOffset.UtcNow;
        var generated = ClientCertificateEnrollment.Generate(
            _options.Country,
            _stateStore.State.DeviceId,
            now);
        client.CertificateDeviceId = EnrollmentDeviceId(
            _stateStore.State.DeviceId,
            now);

        _logger.LogInformation(
            "Requesting a per-device GWM client certificate");
        var response = await client.ApplyCertificateAsync(
            new ApplyCertificateRequest
            {
                Csr = generated.Csr,
                Phone = gwId
            },
            cancellationToken);

        using var issuedCertificate =
            ClientCertificateEnrollment.LoadWithPrivateKey(
                response.Encoded,
                generated.PrivateKey);

        await _stateStore.UpdateAsync(state =>
        {
            state.ClientCertificate = response.Encoded;
            state.ClientPrivateKey = generated.PrivateKey;
        }, cancellationToken);

        client.SetVehicleClientCertificate(issuedCertificate);
        _logger.LogInformation(
            "GWM client certificate enrolled; valid until {NotAfter}",
            issuedCertificate.NotAfter.ToUniversalTime());
    }

    private async Task StoreUserIdentityAsync(
        string? gwId,
        string? beanId,
        CancellationToken cancellationToken)
    {
        if (String.IsNullOrWhiteSpace(gwId) &&
            String.IsNullOrWhiteSpace(beanId))
        {
            return;
        }

        await _stateStore.UpdateAsync(state =>
        {
            if (!String.IsNullOrWhiteSpace(gwId))
            {
                state.GwId = gwId;
            }
            if (!String.IsNullOrWhiteSpace(beanId))
            {
                state.BeanId = beanId;
            }
        }, cancellationToken);
    }

    private async Task LoginAusAsync(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        var request = new AuLoginAccountRequest
        {
            Account = _options.Username,
            Password = _options.Password,
            DeviceId = client.DeviceId,
            Country = _options.Country
        };

        if (!String.IsNullOrWhiteSpace(_options.VerificationCode))
        {
            var code = _options.VerificationCode.Trim();
            try
            {
                await client.CheckSmsCodeAsync(new CheckSmsCode
                {
                    Email = _options.Username,
                    SmsCode = code
                }, cancellationToken);
            }
            catch (GwmApiException ex)
            {
                _logger.LogWarning(
                    "GWM (AU) checkSMSCode returned {Code} {Message}; continuing to login",
                    ex.Code,
                    ex.Message);
            }

            request.VerifyCode = code;
            try
            {
                var response = await client.LoginAccountAusAsync(
                    request,
                    cancellationToken);
                await StoreLoginResponseAsync(client, response, cancellationToken);
                await _supervisorOptions.ClearVerificationCodeAsync(cancellationToken);
                _logger.LogInformation(
                    "GWM (AU) verification code accepted and tokens stored");
            }
            catch (GwmApiException ex)
            {
                await _stateStore.UpdateAsync(
                    state => state.VerificationCodeRequestedAt = null,
                    cancellationToken);
                throw new GwmVerificationRequiredException(
                    "GWM rejected the configured verification_code. Clear it, restart the " +
                    "add-on to request a fresh code, then enter the new code and restart again.",
                    ex);
            }
            return;
        }

        try
        {
            var response = await client.LoginAccountAusAsync(
                request,
                cancellationToken);
            await StoreLoginResponseAsync(client, response, cancellationToken);
        }
        catch (GwmApiException ex) when (IsAusVerificationRequired(ex))
        {
            await RequestVerificationCodeAusAsync(client, cancellationToken);
            throw new GwmVerificationRequiredException(
                "GWM requires e-mail verification for this new device. A code was sent to the " +
                "account e-mail; enter it in the add-on option 'verification_code', save, and restart.",
                ex);
        }
    }

    private async Task RequestVerificationCodeAusAsync(
        GwmApiClient client,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var lastRequest = _stateStore.State.VerificationCodeRequestedAt;
        if (lastRequest.HasValue &&
            now - lastRequest.Value < VerificationCodeRequestInterval)
        {
            return;
        }

        try
        {
            await client.GetSmsCodeAusAsync(
                new AuGetSmsCode { Email = _options.Username },
                cancellationToken);
            await _stateStore.UpdateAsync(
                state => state.VerificationCodeRequestedAt = now,
                cancellationToken);
            _logger.LogWarning(
                "GWM (AU) new-device verification: a code was sent to the account e-mail");
        }
        catch (GwmApiException ex)
        {
            throw new GwmVerificationRequiredException(
                $"GWM (AU) requires verification, but requesting a code failed: {ex.Message}",
                ex);
        }
    }

    private async Task StoreLoginResponseAsync(
        GwmApiClient client,
        LoginAccountResponse response,
        CancellationToken cancellationToken)
    {
        await _stateStore.UpdateAsync(state =>
        {
            var accountChanged =
                !String.IsNullOrWhiteSpace(state.GwId) &&
                !String.Equals(
                    state.GwId,
                    response.GwId,
                    StringComparison.Ordinal);
            state.AccessToken = response.AccessToken;
            state.RefreshToken = response.RefreshToken;
            state.GwId = response.GwId;
            state.BeanId = response.BeanId;
            state.VerificationCodeRequestedAt = null;
            if (accountChanged)
            {
                state.ClientCertificate = null;
                state.ClientPrivateKey = null;
            }
        }, cancellationToken);
        client.SetAccessToken(response.AccessToken);
    }

    private bool RequiresPerDeviceClientCertificate()
    {
        return !String.Equals(_options.Region, "aus", StringComparison.OrdinalIgnoreCase) &&
               !String.Equals(_options.Region, "rus", StringComparison.OrdinalIgnoreCase) &&
               !String.Equals(_options.Region, "cn", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnrollmentDeviceId(
        string stateDeviceId,
        DateTimeOffset now)
    {
        var normalized = (stateDeviceId ?? String.Empty).Replace("-", String.Empty);
        normalized = normalized.Length >= 32
            ? normalized[..32]
            : normalized.PadRight(32, '0');
        return normalized + now.ToUnixTimeMilliseconds();
    }

    private static bool IsEuVerificationRequired(GwmApiException ex)
    {
        return ex.Code is "308103" or "110641" ||
               ex.Message.Contains(
                   "verification code",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAusVerificationRequired(GwmApiException ex)
    {
        return ex.Code is "309702" or "110641" ||
               ex.Message.Contains(
                   "verification code",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRusVerificationRequired(GwmApiException ex)
    {
        return ex.Code == "110641" ||
               ex.Message.Contains(
                   "verification code",
                   StringComparison.OrdinalIgnoreCase);
    }
}
