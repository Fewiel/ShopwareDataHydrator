namespace ShopwareDataHydrator.Models;

public static class EmailHelper
{
    public static string Generate(string firstName, string lastName, string suffix)
    {
        var local = $"{firstName}.{lastName}.{suffix}".ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss")
            .Replace(" ", "").Replace("'", "").Replace("-", "");
        return $"{local}@example.com";
    }
}
