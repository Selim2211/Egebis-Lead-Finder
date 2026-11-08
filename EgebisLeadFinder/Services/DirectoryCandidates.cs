namespace EgebisLeadFinder.Services;

/// <summary>
/// Dizinlerden (Europages, OSB firma listeleri) aday toplama yardimcilari. Dizin sitelerine ISTEK ATILMAZ (Europages kullanim
/// sartlari veri tabani olusturmayi yasaklar; robots.txt botlari engeller): yalnizca Google'in "site:" aramasindaki sonuc
/// basliklarindan firma adi okunur, sonra her firmanin KENDI sitesi ayrica bulunup incelenir.
/// </summary>
public static class DirectoryCandidates
{
    /// <summary>Taranacak dizinler (Turkiye). Europages "uretici" etiketi veriyor; OSB sayfalari bolgedeki fabrikalari listeliyor.</summary>
    public static readonly string[] TurkishDirectories =
    {
        "europages.com.tr",
        "ostim.org.tr",
        "gosb.com.tr",
        "iaosb.org.tr"
    };

    /// <summary>"site:europages.com.tr bulaşık makinesi" — yalnizca duz urun adi, tirnaksiz (dizin basliklari urun adini aynen iceriyor).</summary>
    public static string Query(string site, string term) => $"site:{site} {term}".Trim();

    // Baslik firma adi degil de liste/kategori sayfasi gibiyse (urun adi, "tedarikçiler", "firmalar"...) elenir.
    private static readonly string[] ListingWords =
    {
        "tedarikci", "uretici firma", "ureticileri", "firmalari", "firmalar", "firma listesi", "listesi", "rehberi", "dizini",
        "europages", "kompass", "organize sanayi", "sanayi bolgesi", "ihracatci", "en iyi", "supplier", "manufacturer",
        "companies", "directory", "list of", "ana sayfa", "anasayfa", "iletisim", "hakkimizda", "login", "giris yap", "kayit ol",
        "sayfa ", "page ", "haberler", "duyuru", "etkinlik", "ilanlari"
    };

    private static readonly string[] Separators = { " - ", " – ", " — ", " | ", " :: ", " » ", " · " };

    /// <summary>
    /// Dizin sonuc basligindan firma adini okur ("ARÇELİK A.Ş. - İstanbul - Bulaşık makineleri | Europages" → "ARÇELİK A.Ş.").
    /// Firma adi gibi durmuyorsa (kategori/liste sayfasi, urun adi, cok kisa/uzun) null doner.
    /// </summary>
    public static string? ExtractName(string? title, string product)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var first = title.Trim();
        var cut = Separators.Select(s => first.IndexOf(s, StringComparison.Ordinal)).Where(i => i > 0).DefaultIfEmpty(-1).Min();
        if (cut > 0) first = first[..cut];
        first = first.Trim().Trim('"', '“', '”', '«', '»', ' ');

        if (first.Length is < 3 or > 70) return null;
        if (first.Count(char.IsDigit) > first.Length / 3) return null;

        var norm = TurkishText.Normalize(first);
        if (ListingWords.Any(w => norm.Contains(w))) return null;

        // Baslik aranan urunun kendisi ("Bulaşık Makineleri") ise bu bir kategori sayfasidir.
        var stem = TurkishText.Normalize(product).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4).Select(w => w.Length > 6 ? w[..6] : w).ToList();
        if (stem.Count > 0 && stem.All(s => norm.Contains(s)) && first.Split(' ').Length <= product.Split(' ').Length + 1)
            return null;

        return first;
    }
}
