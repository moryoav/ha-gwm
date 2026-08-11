using GwmOra.Addon.Gwm;

namespace GwmOra.Addon.Tests;

public class CountryCallingCodeResolverTests
{
    [Theory]
    [InlineData("IL", "+972")]
    [InlineData("DE", "+49")]
    [InlineData("GB", "+44")]
    [InlineData("LV", "+371")]
    public void ResolvesKnownCountries(string country, string expected)
    {
        Assert.Equal(expected, CountryCallingCodeResolver.Resolve(country));
    }

    [Fact]
    public void UnknownCountryFailsClearly()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => CountryCallingCodeResolver.Resolve("ZZ"));

        Assert.Contains("No telephone calling code", error.Message);
    }
}
