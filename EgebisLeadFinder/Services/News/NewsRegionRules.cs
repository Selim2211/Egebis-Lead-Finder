using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services.News;

/// <summary>
/// Haberin KONUSU Türkiye mi, yurt dışı mı? (Haberi yazan sitenin ülkesi değil: Türk sitesindeki Tesla haberi Global'dir.)
/// Kesin işaret yoksa null döner; o zaman yapay zekâ karar verir (bkz. NewsScanService).
/// </summary>
public static class NewsRegionRules
{
    private static readonly string[] TurkeyWords =
    {
        "turkiye", "turk", "borsa istanbul", "bist", "kap'a", "osb", "organize sanayi", "tobb", "tuik", "sanayi ve teknoloji bakan",
        "ticaret bakan", "resmi gazete", "tl'lik", "milyon tl", "milyar tl", "bin tl"
    };

    private static readonly string[] ForeignWords =
    {
        "almanya", "abd", "amerika", "cin'", "cin ", "japonya", "fransa", "ingiltere", "italya", "ispanya", "rusya", "ukrayna",
        "hindistan", "guney kore", "kanada", "meksika", "brezilya", "hollanda", "belcika", "isvec", "isvicre", "polonya",
        "avusturya", "yunanistan", "bulgaristan", "romanya", "macaristan", "cekya", "vietnam", "endonezya", "tayvan", "singapur",
        "avustralya", "suudi arabistan", "birlesik arap", "katar", "misir", "israil", "iran", "irak", "azerbaycan", "kazakistan",
        "berlin", "munih", "paris", "londra", "new york", "washington", "pekin", "sanghay", "tokyo", "seul", "brüksel", "bruksel",
        "nasdaq", "wall street", "silikon vadisi", "avrupa birligi", "beyaz saray", "fed "
    };

    // Şehir adı tam kelime olmalı ("Van" → "vantilatör" eşleşmesin); ek kesme işaretiyle gelir: "İzmir'den".
    private static readonly System.Text.RegularExpressions.Regex Cities = new(
        @"(?<![a-z])(" + string.Join("|", Data.TurkishProvinces.All.Select(c => System.Text.RegularExpressions.Regex.Escape(TurkishText.Normalize(c)))) + @")(?=['’s.,;:!?)-]|$)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>"tr", "global" ya da null (karar verilemedi).</summary>
    public static string? Decide(string? text, string? companyCountry = null, string? topicCity = null)
    {
        if (!string.IsNullOrWhiteSpace(companyCountry))
            return TurkishText.Normalize(companyCountry).StartsWith("turkiye", StringComparison.Ordinal) ? NewsRegions.Tr : NewsRegions.Global;
        if (!string.IsNullOrWhiteSpace(topicCity)) return NewsRegions.Tr;

        var t = " " + TurkishText.Normalize(text) + " ";
        // Türk aktör/yer geçiyorsa TR (ör. "İzmir'den Avrupa'ya ihracat").
        if (TurkeyWords.Any(w => t.Contains(w, StringComparison.Ordinal))) return NewsRegions.Tr;
        if (Cities.IsMatch(t)) return NewsRegions.Tr;
        if (ForeignWords.Any(w => t.Contains(" " + w, StringComparison.Ordinal))) return NewsRegions.Global;
        return null;
    }
}
