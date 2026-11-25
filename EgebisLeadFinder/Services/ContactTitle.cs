using System.Text.RegularExpressions;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Kisi unvaninin kalitesi: Google'in kirptigi ("Satış Müdürü ...") ya da aslinda firma adi olan
/// ("Hyundai Motor Türkiye Otomotiv A.Ş.") unvanlari tanir; birden cok kaynaktan en tam olani secer.
/// </summary>
public static partial class ContactTitle
{
    /// <summary>Arama motoru uzun basligi kirpmis: sonu "..." ya da "…".</summary>
    public static bool IsTruncated(string? title) =>
        !string.IsNullOrWhiteSpace(title) && (title.TrimEnd().EndsWith("...", StringComparison.Ordinal) || title.TrimEnd().EndsWith('…'));

    // Sirket hukuki ekleri: "A.Ş.", "Ltd. Şti.", "San. ve Tic.", "GmbH", "Inc." ...
    [GeneratedRegex(@"(?:^|[\s,.(])(?:A\.?\s?Ş\.?|A\.?\s?S\.?|Ltd\.?|Şti\.?|Sti\.?|San\.|Tic\.|Sanayi\s+ve\s+Ticaret|GmbH|Inc\.?|LLC|Corp\.?|S\.?p\.?A\.?|B\.?V\.?|Holding)(?=$|[\s,.)])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompanySuffix();

    // Unvan kelimeleri: icinde bunlardan biri gecen metin firma adi olsa bile unvan sayilir ("CIO BEKO Europe").
    private static readonly string[] RoleWords =
    {
        "müdür", "mudur", "manager", "director", "direktör", "direktor", "başkan", "baskan", "chief", "head", "officer",
        "uzman", "specialist", "mühendis", "muhendis", "engineer", "şef", "sef", "sorumlu", "lead", "lider", "koordinatör",
        "coordinator", "danışman", "consultant", "asistan", "assistant", "analist", "analyst", "temsilci", "yönetici", "yonetici",
        "cio", "cto", "ceo", "cfo", "coo", "founder", "kurucu", "partner", "ortak", "owner", "sahibi", "president", "vp"
    };

    /// <summary>Metin unvan degil, firma adi mi? (hukuki ek tasiyor ve hicbir unvan kelimesi icermiyor)</summary>
    public static bool LooksLikeCompany(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var core = title.TrimEnd(' ', '.', '…');
        if (!CompanySuffix().IsMatch(core)) return false;
        // Kelime basi eslesmesi: "Müdürü", "Manager'ı" gibi ekli halleri de yakalar.
        var words = Regex.Split(TurkishText.Normalize(core), @"[^\p{L}]+").Where(w => w.Length > 0).ToList();
        return !RoleWords.Select(TurkishText.Normalize).Any(r => words.Any(w => w == r || (r.Length >= 4 && w.StartsWith(r, StringComparison.Ordinal))));
    }

    /// <summary>
    /// Mevcut unvanla yeni kaynaktaki unvandan daha iyisini secer. Apollo gibi guvenilir kaynagin tam unvani,
    /// bos / kirpik / firma adi olan unvanin yerine gecer; mevcut unvan zaten tamsa ve daha uzunsa korunur.
    /// </summary>
    public static string? Best(string? current, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || IsTruncated(candidate) || LooksLikeCompany(candidate)) return current;
        if (string.IsNullOrWhiteSpace(current) || IsTruncated(current) || LooksLikeCompany(current)) return candidate.Trim();
        return candidate.Trim().Length >= current.Trim().Length ? candidate.Trim() : current;
    }
}
