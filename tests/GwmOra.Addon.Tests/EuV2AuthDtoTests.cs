using System.Text.Json;
using libgwmapi.DTO.UserAuth;

namespace GwmOra.Addon.Tests;

public class EuV2AuthDtoTests
{
    [Fact]
    public void LoginRequestMatchesCapturedV2PayloadShape()
    {
        var request = new EuLoginWithPasswordRequest
        {
            Account = "owner@example.com",
            CountryCode = "+972",
            Password = "secret",
            DeviceId = "0123456789abcdef",
            Country = "IL",
            VerifyCode = "4873",
            ValidCodeMode = "1"
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request));
        var root = json.RootElement;

        Assert.Equal("owner@example.com", root.GetProperty("account").GetString());
        Assert.Equal("2", root.GetProperty("accountType").GetString());
        Assert.Equal("+972", root.GetProperty("countryCode").GetString());
        Assert.Equal([1, 2], root.GetProperty("agreement").EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray());
        Assert.Equal("secret", root.GetProperty("password").GetString());
        Assert.Equal("0123456789abcdef", root.GetProperty("deviceId").GetString());
        Assert.Equal("0", root.GetProperty("appType").GetString());
        Assert.Equal(String.Empty, root.GetProperty("pushToken").GetString());
        Assert.Equal("IL", root.GetProperty("country").GetString());
        Assert.Equal("4873", root.GetProperty("verifyCode").GetString());
        Assert.Equal("1", root.GetProperty("validCodeMode").GetString());
    }

    [Fact]
    public void LoginRequestOmitsVerificationFieldsBeforeChallenge()
    {
        var request = new EuLoginWithPasswordRequest
        {
            Account = "owner@example.com",
            CountryCode = "+49",
            Password = "secret",
            DeviceId = "0123456789abcdef",
            Country = "DE"
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request));

        Assert.False(json.RootElement.TryGetProperty("verifyCode", out _));
        Assert.False(json.RootElement.TryGetProperty("validCodeMode", out _));
    }
}
