namespace GwmOra.Addon.Gwm;

public static class CountryCallingCodeResolver
{
    private static readonly IReadOnlyDictionary<string, string> CallingCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AD"] = "+376", ["AE"] = "+971", ["AL"] = "+355", ["AM"] = "+374",
            ["AT"] = "+43", ["AU"] = "+61", ["AZ"] = "+994", ["BA"] = "+387",
            ["BE"] = "+32", ["BG"] = "+359", ["BY"] = "+375", ["CH"] = "+41",
            ["CY"] = "+357", ["CZ"] = "+420", ["DE"] = "+49", ["DK"] = "+45",
            ["EE"] = "+372", ["ES"] = "+34", ["FI"] = "+358", ["FO"] = "+298",
            ["FR"] = "+33", ["GB"] = "+44", ["GE"] = "+995", ["GI"] = "+350",
            ["GR"] = "+30", ["HR"] = "+385", ["HU"] = "+36", ["IE"] = "+353",
            ["IL"] = "+972", ["IS"] = "+354", ["IT"] = "+39", ["KZ"] = "+7",
            ["LI"] = "+423", ["LT"] = "+370", ["LU"] = "+352", ["LV"] = "+371",
            ["MC"] = "+377", ["MD"] = "+373", ["ME"] = "+382", ["MK"] = "+389",
            ["MT"] = "+356", ["NL"] = "+31", ["NO"] = "+47", ["NZ"] = "+64",
            ["PL"] = "+48", ["PT"] = "+351", ["RO"] = "+40", ["RS"] = "+381",
            ["RU"] = "+7", ["SE"] = "+46", ["SI"] = "+386", ["SK"] = "+421",
            ["SM"] = "+378", ["TR"] = "+90", ["UA"] = "+380", ["UK"] = "+44",
            ["VA"] = "+39", ["ZA"] = "+27"
        };

    public static string Resolve(string country)
    {
        var normalized = (country ?? String.Empty).Trim().ToUpperInvariant();
        if (CallingCodes.TryGetValue(normalized, out var callingCode))
        {
            return callingCode;
        }

        throw new InvalidOperationException(
            $"No telephone calling code is known for country '{normalized}'.");
    }
}
