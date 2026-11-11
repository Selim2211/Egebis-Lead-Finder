using System.Text.Json;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services.CompanyIntel;

/// <summary>
/// Arama sonucunun GERCEKTEN bu firmayla ilgili olup olmadigini denetler. Firma adi kisa/yaygin oldugunda
/// ("Uğur") Google baska firmalarin haberini getirir: ornegin "UĞUR GÜRBÜZ CIVATA" konkordato ilani, "Uğur Soğutma"ya
/// "Riskli" diye eklenmisti. Kabul kurallari:
///  1. Sonucun adresi firmanin kendi sitesi ise ya da metinde firmanin alan adi geciyorsa: kabul.
///  2. Firma adi metinde (kelime butunu olarak) gecmiyorsa: ret.
///  3. Ad birden fazla kelimeyse ("Trakya Cam"): kabul.
///  4. Ad tek kelimeyse ("Uğur", "Ege"): metinde firmaya ozgu bir ipucu da olmali (sektor, urun veya sehir kelimesi); yoksa ret.
/// </summary>
public static class SnippetRelevance
{
    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "uretim", "uretimi", "imalat", "imalati", "sanayi", "sanayii", "ticaret", "hizmet", "hizmetleri", "sistem", "sistemleri",
        "urun", "urunleri", "firma", "sirket", "sirketi", "grup", "holding", "turkiye", "makine", "makina", "makineleri",
        "ve", "ile", "icin", "genel", "ozel", "diger", "toptan", "perakende", "satis"
    };

    public static bool IsAbout(Company company, string? text, string? url)
    {
        var key = CompanyNameMatch.Key(company.Name);
        if (key.Length < 2) return true; // ad yok: eleme yapilamaz

        var host = TurkishText.Normalize(HostOf(url) ?? string.Empty);
        var domain = TurkishText.Normalize((company.Domain ?? string.Empty).Replace("www.", string.Empty));
        if (domain.Length > 0 && (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal))) return true;

        var normalized = TurkishText.Normalize(text);
        if (domain.Length > 0 && normalized.Contains(domain, StringComparison.Ordinal)) return true;

        if (!ContainsWhole(normalized, key)) return false;
        if (key.Contains(' ')) return true;

        return Anchors(company).Any(a => ContainsWhole(normalized, a));
    }

    /// <summary>Tek kelimelik adi firmaya baglayan ipuclari: sektor ve urun kelimeleri ile sehir.</summary>
    public static List<string> Anchors(Company company)
    {
        var words = new List<string>();
        void AddWords(string? phrase)
        {
            foreach (var w in TurkishText.Normalize(phrase).Split(new[] { ' ', ',', '/', '-', '(', ')', '.' }, StringSplitOptions.RemoveEmptyEntries))
                if (w.Length >= 4 && !GenericWords.Contains(w) && !words.Contains(w)) words.Add(w);
        }

        AddWords(company.Industry);
        AddWords(company.City);
        if (!string.IsNullOrWhiteSpace(company.AiAnalysis))
        {
            try
            {
                var analysis = JsonSerializer.Deserialize<CompanyAnalysis>(company.AiAnalysis, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (analysis is not null)
                {
                    AddWords(analysis.Industry);
                    foreach (var p in analysis.Products.Take(12)) AddWords(p);
                }
            }
            catch (JsonException) { /* ipucu olmadan devam */ }
        }
        return words;
    }

    private static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant().Replace("www.", string.Empty) : null;

    private static bool ContainsWhole(string text, string needle)
    {
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            var startOk = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            var end = i + needle.Length;
            if (startOk && (end == text.Length || !char.IsLetterOrDigit(text[end]))) return true;
        }
        return false;
    }
}
