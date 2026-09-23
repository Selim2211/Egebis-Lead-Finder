namespace EgebisLeadFinder.Services.CompanyIntel;

/// <summary>
/// Firma adi eslesme yardimcisi. KAP uye listesi ve İSO/Capital/TİM referans
/// listeleriyle karsilastirmada kullanilir: Turkce harf farklarini yok sayar,
/// hukuki ek ve jenerik kelimeleri atar.
/// </summary>
public static class CompanyNameMatch
{
    private static readonly string[] Noise =
    {
        "anonim sirketi", "limited sirketi", "a.s.", "a.s",
        "ltd. sti.", "ltd sti", "ltd.", "sti.", "ltd", "sti",
        "sanayii", "sanayi", "ticaret", "san.", "tic.", "san", "tic",
        "holding", "grubu", "grup", " ve "
    };

    /// <summary>
    /// Karsilastirma anahtari: kucuk harf + ASCII + gurultu kelimeler cikarilmis.
    /// Ornek: "Trakya Cam Sanayii A.Ş." ve "TRAKYA CAM SANAYİİ A.Ş." -> "trakya cam".
    /// </summary>
    public static string Key(string? name)
    {
        var s = TurkishText.Normalize(name);

        foreach (var noise in Noise)
            s = s.Replace(noise, " ");

        return string.Join(' ',
            s.Split(new[] { ' ', '.', ',', '-', '/', '&' }, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Iki firma adi ayni firmayi mi gosteriyor? Anahtarlar birebir esitse veya
    /// biri digerini kelime butunu olarak iceriyorsa (en az 3 karakter) eslesme sayilir.
    /// </summary>
    public static bool IsMatch(string? a, string? b)
    {
        var ka = Key(a);
        var kb = Key(b);

        if (ka.Length < 3 || kb.Length < 3) return false;
        if (ka == kb) return true;

        return Contains(ka, kb) || Contains(kb, ka);
    }

    /// <summary>haystack, needle'i kelime siniri koruyarak iceriyor mu?</summary>
    private static bool Contains(string haystack, string needle)
    {
        var idx = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var beforeOk = idx == 0 || haystack[idx - 1] == ' ';
            var after = idx + needle.Length;
            var afterOk = after >= haystack.Length || haystack[after] == ' ';
            if (beforeOk && afterOk) return true;
            idx = haystack.IndexOf(needle, idx + 1, StringComparison.Ordinal);
        }
        return false;
    }
}
