namespace ShopwareDataHydrator.Models;

public static class LocaleHelper
{
    private static readonly Dictionary<string, string> IsoToLocale = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DE"] = "de",
        ["AT"] = "de_AT",
        ["CH"] = "de_CH",
        ["US"] = "en",
        ["GB"] = "en_GB",
        ["AU"] = "en_AU",
        ["IE"] = "en",
        ["CA"] = "en",
        ["FR"] = "fr",
        ["BE"] = "fr",
        ["NL"] = "nl",
        ["IT"] = "it",
        ["ES"] = "es",
        ["PT"] = "pt_PT",
        ["PL"] = "pl",
        ["CZ"] = "cz",
        ["SK"] = "cz",
        ["SE"] = "sv",
        ["DK"] = "da",
        ["NO"] = "nb_NO",
        ["FI"] = "fi",
        ["RO"] = "ro",
        ["HR"] = "hr",
        ["UA"] = "uk",
        ["RU"] = "ru",
        ["TR"] = "tr",
        ["JP"] = "ja",
        ["KR"] = "ko",
        ["BR"] = "pt_BR",
        ["MX"] = "es_MX",
        ["AR"] = "es",
    };

    public static string GetBogusLocale(string countryIso)
    {
        return IsoToLocale.TryGetValue(countryIso, out var locale) ? locale : "en";
    }
}
