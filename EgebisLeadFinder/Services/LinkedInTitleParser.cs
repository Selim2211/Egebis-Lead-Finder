namespace EgebisLeadFinder.Services;

/// <summary>
/// Serper'in LinkedIn arama sonucu dondurdugu basligi ("Ad Soyad - Ünvan - Firma | LinkedIn")
/// ayri alanlara ayirir. Format tutarli degildir; unvan veya firma parcasi eksik olabilir.
/// </summary>
public static class LinkedInTitleParser
{
    /// <summary>Arama motorunun uzun basligi kirptigini gosteren sonekler.</summary>
    private static readonly string[] EllipsisSuffixes = { "...", "…" };

    /// <summary>
    /// Ozet metninde bir cumlecigin bittigi yerler. Ellipsis de sinir sayilir:
    /// ozetin kendisi kirpik olabilir ve devamindaki cumleyi unvana yapistirmak
    /// alakasiz metin uretir (gozlemlenen vaka: "Kıdemli Kalite ... SAP PP Module.
    /// Orhan Holding. Kas 2018 tarihinde verildi." tek bir unvan sanilmisti).
    /// </summary>
    private static readonly string[] ClauseBreaks =
        { " · ", " | ", "\n", " — ", " – ", ". ", " ; ", " , ", "...", "…" };

    /// <summary>Kurtarilan metin bundan uzunsa unvan degil, cumle yakalamisizdir.</summary>
    private const int MaxRecoveredTitleLength = 120;

    /// <summary>
    /// Turkce LinkedIn basliklarinda sik gorulen kalip: "&lt;Firma&gt; şirketinde &lt;Ünvan&gt;".
    /// Bu durumda unvan olarak yalnizca sagdaki kisim anlamlidir.
    /// </summary>
    private const string CompanyTitleSeparator = " şirketinde ";

    public static LinkedInProfile? Parse(string? title, string? url, string? snippet = null)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        // " | LinkedIn" sonekini (veya benzerlerini) at.
        var cleaned = title.Split('|')[0].Trim();
        if (cleaned.Length == 0) return null;

        var parts = cleaned
            .Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToArray();

        if (parts.Length == 0) return null;

        var rawTitle = parts.Length > 1 ? parts[1] : null;
        var company = parts.Length > 2 ? parts[2] : null;

        // Once "<Firma> şirketinde <Ünvan>" kalibindaki firma adi ayrilir. Bu sira
        // onemli: ayirmadan once kalan metin ("TRAKYA DÖKÜM ... şirketinde Sap")
        // ozette hicbir zaman aynen gecmez, ayirdiktan sonraki cekirdek ("Sap") gecer.
        var (splitTitle, titleCompany) = SplitCompanyPrefix(rawTitle);

        // Arama motoru basligi kirpmis olabilir ("Sap ..."). Ozet metni ayni
        // unvani cogu zaman tam icerir; oradan tamamlamayi dene.
        var finalTitle = RecoverTruncated(splitTitle, snippet);

        // Baslik tamamen kirpilmissa geriye sadece "..." kalir; bu bir unvan degil.
        if (!HasLetters(finalTitle)) finalTitle = null;

        return new LinkedInProfile
        {
            Name = parts[0],
            Title = finalTitle,
            Company = company ?? titleCompany,
            ProfileUrl = url
        };
    }

    /// <summary>
    /// Basligin sonu "..." ile bitiyorsa, kalan cekirdegi ozette kelime sinirinda
    /// arar ve cumlecik sonuna kadar uzatir. Ornek: baslik "Sap ...", ozet
    /// "Deneyim ; Sap Advanced Business Application Programming Developer. TRAKYA..."
    /// -> "Sap Advanced Business Application Programming Developer".
    ///
    /// Ozette bulunamazsa kirpilmis hali oldugu gibi kalir; eksik bilgi uydurulmaz.
    /// Cekirdek kisa olabildigi icin ("Sap") yanlis eslesme ihtimali vardir; bu yuzden
    /// sonuc uzunluk siniriyla kisitlanir ve yalnizca daha fazla bilgi getirdiginde kabul edilir.
    /// </summary>
    internal static string? RecoverTruncated(string? title, string? snippet)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(snippet))
            return title;

        var suffix = EllipsisSuffixes.FirstOrDefault(s => title.TrimEnd().EndsWith(s, StringComparison.Ordinal));
        if (suffix is null) return title;

        var stem = title.TrimEnd();
        stem = stem[..^suffix.Length].TrimEnd(' ', '.', ',', '-');

        // Tek/iki harflik cekirdek ozette rastgele yerlere eslesir.
        if (stem.Length < 3) return title;

        var start = FindWordStart(snippet, stem);
        if (start < 0) return title;

        var rest = snippet[start..];

        // Cumleciğin bittigi ilk isarete kadar al.
        var end = ClauseBreaks
            .Select(b => rest.IndexOf(b, StringComparison.Ordinal))
            .Where(i => i > 0)
            .DefaultIfEmpty(-1)
            .Min();

        var recovered = (end > 0 ? rest[..end] : rest).Trim().TrimEnd('.', ',', ';', '·', '-');

        // Kurtarma yalnizca gercekten daha fazla bilgi getirdiyse ve makul
        // uzunlukta kaldiysa kabul edilir; yoksa unvan yerine cumle kaydedebilirdik.
        return recovered.Length > stem.Length && recovered.Length <= MaxRecoveredTitleLength
            ? recovered
            : title;
    }

    /// <summary>Metinde en az bir harf var mi? Sadece noktalama unvan sayilmaz.</summary>
    private static bool HasLetters(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Any(char.IsLetter);

    /// <summary>
    /// Cekirdegi metinde kelime basi olacak sekilde arar: "Sap" ararken
    /// "Sapanca" icindeki parcayi eslestirmemek icin.
    /// </summary>
    private static int FindWordStart(string haystack, string needle)
    {
        var index = haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var afterIndex = index + needle.Length;
            var afterOk = afterIndex >= haystack.Length || !char.IsLetterOrDigit(haystack[afterIndex]);

            if (beforeOk && afterOk) return index;

            index = haystack.IndexOf(needle, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return -1;
    }

    /// <summary>
    /// "&lt;Firma&gt; şirketinde &lt;Ünvan&gt;" kalibini ayirir. Kalip yoksa unvan
    /// oldugu gibi doner, firma null kalir.
    /// </summary>
    internal static (string? Title, string? Company) SplitCompanyPrefix(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return (title, null);

        var index = title.IndexOf(CompanyTitleSeparator, StringComparison.OrdinalIgnoreCase);
        if (index <= 0) return (title, null);

        var company = title[..index].Trim();
        var role = title[(index + CompanyTitleSeparator.Length)..].Trim();

        // Sagda anlamli bir unvan kalmadiysa bolmenin faydasi yok.
        return role.Length == 0 ? (title, null) : (role, company);
    }
}

/// <summary>LinkedIn arama sonucundan ayristirilan aday kisi.</summary>
public class LinkedInProfile
{
    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Company { get; set; }
    public string? ProfileUrl { get; set; }

    /// <summary>
    /// Apollo'nun ad/soyad ayrik istedigi icin bolunur. Tek kelimelik isimlerde
    /// (unvan/sirket adi yanlislikla isim sanildiginda) soyad bos doner.
    /// </summary>
    public (string First, string Last) SplitName()
    {
        var bits = Name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return bits.Length == 2 ? (bits[0], bits[1]) : (Name, string.Empty);
    }
}
