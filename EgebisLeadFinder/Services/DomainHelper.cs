namespace EgebisLeadFinder.Services;

/// <summary>URL normalizasyonu ve domain cikarma yardimcilari.</summary>
public static class DomainHelper
{
    /// <summary>URL'den "www." onekini atilmis, kucuk harfli host doner. Basarisizsa null.</summary>
    public static string? ExtractDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.") ? host[4..] : host;
    }

    /// <summary>Domainden okunabilir firma adi tahmini uretir: "abcotomotiv.com.tr" -> "Abcotomotiv".</summary>
    public static string GuessNameFromDomain(string domain)
    {
        var first = domain.Split('.').FirstOrDefault() ?? domain;
        return first.Length == 0 ? domain : char.ToUpperInvariant(first[0]) + first[1..];
    }

    /// <summary>Kara listedeki bir domain mi? Alt alan adlarini da kapsar.</summary>
    public static bool IsBlocked(string domain, IEnumerable<string> blockedDomains) =>
        blockedDomains.Any(b => domain.Equals(b, StringComparison.OrdinalIgnoreCase)
                             || domain.EndsWith("." + b, StringComparison.OrdinalIgnoreCase)
                             || domain.Contains(b, StringComparison.OrdinalIgnoreCase));
}
