namespace EgebisLeadFinder.Services;

/// <summary>
/// Turkce metin karsilastirma yardimcisi.
/// ToLowerInvariant() Turkce "İ" harfini birlesik noktali "i̇" haline getirir ve
/// duz string karsilastirmasi tutmaz. Bu yuzden karsilastirmalarda once
/// aksanlar sadelestirilir.
/// </summary>
public static class TurkishText
{
    /// <summary>Turkce harfleri ASCII karsiliklarina indirger ve kucuk harfe cevirir.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var buffer = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            buffer[i] = value[i] switch
            {
                'İ' or 'I' or 'ı' or 'i' => 'i',
                'Ş' or 'ş' => 's',
                'Ğ' or 'ğ' => 'g',
                'Ü' or 'ü' => 'u',
                'Ö' or 'ö' => 'o',
                'Ç' or 'ç' => 'c',
                var c => char.ToLowerInvariant(c)
            };
        }

        return new string(buffer);
    }

    /// <summary>Turkce harf farklarini yok sayarak alt dize aramasi yapar.</summary>
    public static bool ContainsNormalized(string? haystack, string? needle)
    {
        if (string.IsNullOrEmpty(needle)) return false;
        return Normalize(haystack).Contains(Normalize(needle), StringComparison.Ordinal);
    }

    /// <summary>
    /// Anahtari kelime sinirinda arar. Duz alt dize aramasi kisa anahtarlarda
    /// yanlis eslesme uretiyor: "Automocion" icinde "cio", "Directors" icinde "cto".
    /// </summary>
    public static bool ContainsWord(string? haystack, string? needle)
    {
        if (string.IsNullOrEmpty(needle)) return false;

        var text = Normalize(haystack);
        var word = Normalize(needle);
        if (word.Length == 0 || text.Length < word.Length) return false;

        var index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            var beforeOk = index == 0 || !IsWordChar(text[index - 1]);
            var afterIndex = index + word.Length;
            var afterOk = afterIndex >= text.Length || !IsWordChar(text[afterIndex]);

            if (beforeOk && afterOk) return true;

            index++;
        }

        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);
}
