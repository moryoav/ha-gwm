using libgwmapi.DTO.AppAuth;
using libgwmapi.DTO.User;
using libgwmapi.DTO.UserAuth;

namespace libgwmapi;

public partial class GwmApiClient
{
    public Task<CustomerServicePhone> GetCustomerServicePhoneAsync(
        string countryCode,
        CancellationToken cancellationToken)
    {
        return GetH5Async<CustomerServicePhone>(
            $"userAuth/customerServicePhone?countryCode={countryCode}",
            cancellationToken);
    }

    public Task GetSmsCodeAsync(
        GetSmsCode request,
        CancellationToken cancellationToken)
    {
        return PostH5Async("userAuth/getSMSCode", request, cancellationToken);
    }

    public Task<LoginAccountResponse> LoginWithSmsAsync(
        LoginWithSmsRequest request,
        CancellationToken cancellationToken)
    {
        return PostH5Async<LoginWithSmsRequest, LoginAccountResponse>(
            "userAuth/loginWithSMS",
            request,
            cancellationToken);
    }

    public Task<LoginAccountResponse> LoginAccountAsync(
        LoginAccountRequest request,
        CancellationToken cancellationToken)
    {
        return PostH5Async<LoginAccountRequest, LoginAccountResponse>(
            "userAuth/loginAccount",
            request,
            cancellationToken);
    }

    public Task<LoginAccountResponse> LoginWithPasswordV2Async(
        EuLoginWithPasswordRequest request,
        CancellationToken cancellationToken)
    {
        return PostAuthAsync<EuLoginWithPasswordRequest, LoginAccountResponse>(
            "userAuth/loginWithPassword",
            request,
            cancellationToken);
    }

    public Task GetVerifyCodeV2Async(
        EuGetVerifyCodeRequest request,
        CancellationToken cancellationToken)
    {
        return PostAuthAsync("userAuth/getVerifyCode", request, cancellationToken);
    }

    public Task CheckVerifyCodeV2Async(
        EuCheckVerifyCodeRequest request,
        CancellationToken cancellationToken)
    {
        return PostAuthAsync("userAuth/checkVerifyCode", request, cancellationToken);
    }

    public Task<ApplyCertificateResponse> ApplyCertificateAsync(
        ApplyCertificateRequest request,
        CancellationToken cancellationToken)
    {
        return PostCertificateAsync<ApplyCertificateRequest, ApplyCertificateResponse>(
            "appAuth/applyCertificate",
            request,
            cancellationToken);
    }

    public Task<LoginAccountResponse> LoginAccountAusAsync(
        AuLoginAccountRequest request,
        CancellationToken cancellationToken)
    {
        return PostH5Async<AuLoginAccountRequest, LoginAccountResponse>(
            "userAuth/loginAccount",
            request,
            cancellationToken);
    }

    public Task GetSmsCodeAusAsync(
        AuGetSmsCode request,
        CancellationToken cancellationToken)
    {
        return PostH5Async("userAuth/getSMSCode", request, cancellationToken);
    }

    public Task CheckSmsCodeAsync(
        CheckSmsCode request,
        CancellationToken cancellationToken)
    {
        return PostH5Async("userAuth/checkSMSCode", request, cancellationToken);
    }

    public Task CheckSecurityPasswordAsync(
        CheckSecurityPassword request,
        CancellationToken cancellationToken)
    {
        return PostH5Async(
            "userAuth/checkSecurityPassword",
            request,
            cancellationToken);
    }

    public Task AddAppDeviceInfoAsync(
        AddAppDevice request,
        CancellationToken cancellationToken)
    {
        return PostH5Async("userAuth/addAppDeviceInfo", request, cancellationToken);
    }

    public Task<RefreshTokenResponse> RefreshTokenAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        return PostH5Async<RefreshTokenRequest, RefreshTokenResponse>(
            "userAuth/refreshToken",
            request,
            cancellationToken);
    }
}
