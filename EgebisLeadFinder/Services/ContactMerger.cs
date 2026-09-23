using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Farkli kaynaklardan (firma sitesi, Apollo, LinkedIn) gelen kisileri birlestirir.
/// Kesif ve zenginlestirme ayri adimlar oldugu icin bu islem iki yerden cagrilir.
/// </summary>
public static class ContactMerger
{
    // Apollo listeleme artik kredisiz oldugu icin (bkz. HybridContactEnrichmentService)
    // TOFAŞ gibi buyuk firmalarda onlarca aday birikebilir; eski 10 siniri bunu keserdi.
    private const int MaxContactsPerCompany = 60;

    /// <summary>Arama motorunun unvani kirptigini gosteren sonekler.</summary>
    private static readonly string[] EllipsisSuffixes = { "...", "…" };

    /// <summary>
    /// Ayni kisi (ada gore) birden fazla kaynakta varsa tek kayda indirilir.
    /// Kayitlardan biri secilip digeri atilmaz: her alan icin en iyi deger
    /// alinir. Boylece e-postasi eski kayittan, tam unvani yeni kayittan gelen
    /// bir kisi ikisini de korur — aksi halde daha iyi bir unvan bulunsa bile
    /// e-postasi olan eski kayit onu bastirirdi.
    /// </summary>
    public static List<Contact> Merge(IEnumerable<Contact> existing, IEnumerable<Contact> incoming)
    {
        return existing
            .Concat(incoming)
            // ApolloId varsa oncelikle onunla grupla: "api_search" ayni kisiyi her
            // cagrida ayni obfuscated isimle ("Eren Ak***y") dondurur, ama e-postasi
            // acildiktan sonra Name gercek isimle degisir ("Eren Aksoy"). Isme gore
            // gruplasaydik bu ayni kisi ikinci "Lead'leri Bul"da yeniden (obfuscated)
            // eklenip mukerrerlesirdi; kimlik ApolloId'de sabit kalir.
            .GroupBy(c => !string.IsNullOrWhiteSpace(c.ApolloId)
                ? $"apollo:{c.ApolloId}"
                : (c.Name ?? c.Email ?? Guid.NewGuid().ToString()).Trim().ToLowerInvariant())
            .Select(MergeGroup)
            .OrderByDescending(c => c.TitleScore)
            .Take(MaxContactsPerCompany)
            .ToList();
    }

    /// <summary>
    /// Ayni kisiye ait kayitlari tek kayda indirir. Temel kayit (kimligi ve veritabani
    /// satirini korumak icin) e-postasi olan ve unvan puani yuksek olandir; eksik
    /// alanlar digerlerinden tamamlanir.
    /// </summary>
    private static Contact MergeGroup(IGrouping<string, Contact> group)
    {
        var candidates = group.ToList();
        if (candidates.Count == 1) return candidates[0];

        var merged = candidates
            .OrderByDescending(c => !string.IsNullOrWhiteSpace(c.Email) ? 1 : 0)
            .ThenByDescending(c => c.TitleScore)
            .First();

        merged.Email ??= candidates.Select(c => c.Email).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        merged.Phone ??= candidates.Select(c => c.Phone).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        merged.SourceUrl ??= candidates.Select(c => c.SourceUrl).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        merged.ApolloId ??= candidates.Select(c => c.ApolloId).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        merged.Title = PickBestTitle(candidates) ?? merged.Title;
        merged.TitleScore = candidates.Max(c => c.TitleScore);

        return merged;
    }

    /// <summary>
    /// En iyi unvan: kirpilmamis olan kirpilmisa yeglenir; esitlikte daha uzun
    /// (daha fazla bilgi tasiyan) metin secilir.
    /// </summary>
    private static string? PickBestTitle(List<Contact> candidates) =>
        candidates
            .Select(c => c.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .OrderByDescending(t => IsTruncated(t!) ? 0 : 1)
            .ThenByDescending(t => t!.Length)
            .FirstOrDefault();

    private static bool IsTruncated(string title) =>
        EllipsisSuffixes.Any(s => title.TrimEnd().EndsWith(s, StringComparison.Ordinal));
}
