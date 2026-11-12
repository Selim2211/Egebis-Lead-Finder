using System.Security.Cryptography;
using System.Text;

namespace EgebisLeadFinder.Services.News;

/// <summary>Aynı haberin iki kez kaydedilmesini (ve iki kez yapay zekâya gitmesini) önleyen anahtarlar.</summary>
public static class NewsDedup
{
    private static readonly string[] TrackingParams = { "utm_", "oc", "hl", "gl", "ceid", "fbclid", "gclid" };

    /// <summary>Adresin küçük harfli ve izleme parametresiz halinin SHA-256 özeti (64 hex).</summary>
    public static string UrlHash(string url)
    {
        var normalized = (url ?? string.Empty).Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            var query = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !TrackingParams.Any(t => p.StartsWith(t, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p, StringComparer.Ordinal);
            normalized = $"{uri.Host.ToLowerInvariant().Replace("www.", string.Empty)}{uri.AbsolutePath.TrimEnd('/')}?{string.Join("&", query)}";
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToLowerInvariant()))).ToLowerInvariant();
    }

    /// <summary>Başlığın harf ve rakamdan oluşan, Türkçe harfleri sadeleştirilmiş kısa anahtarı.</summary>
    public static string TitleKey(string title)
    {
        var sb = new StringBuilder();
        foreach (var ch in TurkishText.Normalize(title))
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.Length > 200 ? sb.ToString(0, 200) : sb.ToString();
    }
}
