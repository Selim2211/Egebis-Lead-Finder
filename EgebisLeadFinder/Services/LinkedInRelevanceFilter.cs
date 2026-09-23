namespace EgebisLeadFinder.Services;

/// <summary>
/// Google'in "site:" + tam ifade aramasi bazen alakasiz LinkedIn sonuclari da getirir:
/// sorguya yazilan firma adi, donen sonucun basliginda/ozetinde hic gecmeyebilir
/// (orn. "SAP" gibi genel bir anahtar kelimeyle eslesip firma adini gormezden gelmesi).
/// Bu sinif, Apollo'ya sormadan once sonucun gercekten o firmayla ilgili oldugunu
/// dogrular — gozlemlenen gercek vaka: "Toksan Otomotiv" aramasinda, Toksan ile hicbir
/// ilgisi olmayan bir SAP danismani, sadece "SAP" kelimesi eslestigi icin sonuca girmisti.
/// </summary>
public static class LinkedInRelevanceFilter
{
    // Firma unvanlarindaki hukuki ek ve jenerik sektor kelimeleri; bunlar tek basina
    // hangi firma oldugunu ayirt etmez, o yuzden anahtar kelime secerken atlanir.
    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a.ş", "a.ş.", "as", "ltd", "ltd.", "şti", "şti.", "sti", "san", "san.",
        "tic", "tic.", "ve", "sanayi", "ticaret", "otomotiv", "holding", "grup",
        "grubu", "türk", "türkiye", "endüstri", "makina", "makine", "metal",
        "plastik", "elektronik", "teknoloji", "sistemleri", "yatırım"
    };

    /// <summary>
    /// Sonuc basligi + ozetinde firmanin ayirt edici kelimesi geciyor mu?
    /// Ayirt edici kelime bulunamazsa (tum kelimeler jenerik) dogrulama yapilamaz;
    /// bu durumda sonuc bloklanmaz, yanlislikla gecerli adaylari elememek icin.
    /// </summary>
    public static bool MentionsCompany(string? companyName, string? title, string? snippet)
    {
        var keyword = ExtractDistinctiveKeyword(companyName);
        if (string.IsNullOrEmpty(keyword)) return true;

        var haystack = $"{title} {snippet}";
        return TurkishText.ContainsWord(haystack, keyword);
    }

    /// <summary>
    /// Firma adindaki en ayirt edici kelimeyi secer: hukuki ek ve jenerik sektor
    /// kelimelerini atlayip ilk anlamli kelimeyi doner.
    /// Ornek: "Toksan Otomotiv A.Ş." -> "Toksan".
    /// </summary>
    public static string? ExtractDistinctiveKeyword(string? companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName)) return null;

        return companyName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(',', '.', '-'))
            .FirstOrDefault(w => w.Length > 2 && !GenericWords.Contains(w));
    }
}
