using GwmOra.Addon.Configuration;

namespace GwmOra.Addon.Tests;

public class AddonOptionsLoaderTests
{
    [Fact]
    public void LoadNormalizesAndValidatesOptions()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
        {
          "country": "de",
          "region": " AUS ",
          "username": "owner@example.com",
          "password": "secret",
          "verification_code": " 1234 ",
          "security_pin": "123456",
          "enable_remote_commands": true,
          "enable_charging_control": true,
          "poll_interval_seconds": 120,
          "log_level": "debug"
        }
        """);

        var options = AddonOptionsLoader.Load(path);

        Assert.Equal("DE", options.Country);
        Assert.Equal("aus", options.Region);
        Assert.Equal("owner@example.com", options.Username);
        Assert.Equal("1234", options.VerificationCode);
        Assert.True(options.EnableRemoteCommands);
        Assert.True(options.EnableChargingControl);
        Assert.Equal(120, options.PollIntervalSeconds);
        Assert.Equal("debug", options.LogLevel);
    }

    [Fact]
    public void ChargingControlDefaultsToDisabled()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
        {
          "country": "DE",
          "region": "aus",
          "username": "owner@example.com",
          "password": "secret",
          "poll_interval_seconds": 60,
          "log_level": "info"
        }
        """);

        var options = AddonOptionsLoader.Load(path);

        Assert.False(options.EnableChargingControl);
    }

    [Fact]
    public void LoadDefaultsRegionToEu()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
        {
          "country": "DE",
          "username": "owner@example.com",
          "password": "secret",
          "poll_interval_seconds": 60,
          "log_level": "info"
        }
        """);

        var options = AddonOptionsLoader.Load(path);

        Assert.Equal("eu", options.Region);
    }

    [Fact]
    public void LoadAcceptsRusRegion()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
        {
          "country": "RU",
          "region": "rus",
          "username": "owner@example.com",
          "password": "secret",
          "poll_interval_seconds": 60,
          "log_level": "info"
        }
        """);

        var options = AddonOptionsLoader.Load(path);

        Assert.Equal("RU", options.Country);
        Assert.Equal("rus", options.Region);
    }

    [Fact]
    public void LoadAcceptsChinaPhoneLoginWithoutPassword()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
        {
          "country": "CN",
          "region": "cn",
          "username": "13800138000",
          "password": "",
          "verification_code": "123456",
          "poll_interval_seconds": 60,
          "log_level": "info"
        }
        """);

        var options = AddonOptionsLoader.Load(path);

        Assert.Equal("CN", options.Country);
        Assert.Equal("cn", options.Region);
        Assert.Equal("13800138000", options.Username);
        Assert.Equal("123456", options.VerificationCode);
        Assert.Empty(options.Password);
    }

    [Fact]
    public void LoadRequiresChinaCountryForChinaRegion()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
        {
          "country": "DE",
          "region": "cn",
          "username": "13800138000",
          "password": "",
          "poll_interval_seconds": 60,
          "log_level": "info"
        }
        """);

        var error = Assert.Throws<InvalidOperationException>(() => AddonOptionsLoader.Load(path));

        Assert.Contains("must be 'CN'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsUnsupportedRegion()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
        {
          "country": "DE",
          "region": "us",
          "username": "owner@example.com",
          "password": "secret",
          "poll_interval_seconds": 60,
          "log_level": "info"
        }
        """);

        var error = Assert.Throws<InvalidOperationException>(() => AddonOptionsLoader.Load(path));

        Assert.Contains("'eu', 'aus', 'rus', or 'cn'", error.Message);
    }

    [Fact]
    public void LoadRejectsMissingCredentials()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
        {
          "country": "DE",
          "username": "",
          "password": "",
          "poll_interval_seconds": 60,
          "log_level": "info"
        }
        """);

        Assert.Throws<InvalidOperationException>(() => AddonOptionsLoader.Load(path));
    }
}
