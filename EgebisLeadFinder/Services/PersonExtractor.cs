using System.Text.RegularExpressions;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Sayfa metninden "isim + unvan" ciftlerini cikarir.
/// AI #2 yerine kod tabanli yaklasim (dokuman bolum 25).
/// </summary>
public static partial class PersonExtractor
{
    // Turkce ve Ingilizce unvan anahtar kelimeleri.
    private static readonly string[] TitleKeywords =
    {
        "genel müdür", "genel müdür yardımcısı", "fabrika müdürü", "üretim müdürü",
        "bilgi işlem müdürü", "bilgi teknolojileri müdürü", "bilgi işlem sorumlusu",
        "it müdürü", "it manager", "it director", "cio", "cto", "ceo",
        "satın alma müdürü", "kalite müdürü", "planlama müdürü",
        "sap danışmanı", "sap uzmanı", "production manager", "plant manager",
        "yönetim kurulu başkanı", "genel koordinatör", "operasyon müdürü"
    };

    // Iki-dort kelimelik, her kelimesi buyuk harfle baslayan Turkce isim kalibi.
    [GeneratedRegex(@"\b[A-ZÇĞİÖŞÜ][a-zçğıöşü]{1,20}(?:\s+[A-ZÇĞİÖŞÜ][a-zçğıöşü]{1,20}){1,2}\b", RegexOptions.Compiled)]
    private static partial Regex NameRegex();

    /// <summary>
    /// Unvan gecen satirlarin cevresinde isim arar.
    /// Isim bulunamazsa kisi yine dondurulur (unvan tek basina da degerlidir).
    /// </summary>
    public static List<(string Name, string Title)> Extract(string text)
    {
        var results = new List<(string, string)>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length > 120) continue; // uzun paragraf, muhtemelen metin govdesi

            if (LooksLikeMenu(line)) continue;

            var matchedTitle = TitleKeywords.FirstOrDefault(k => TurkishText.ContainsWord(line, k));
            if (matchedTitle is null) continue;

            // Isim ayni satirda, bir onceki veya bir sonraki satirda olabilir.
            var candidates = new List<string> { line };
            if (i > 0) candidates.Add(lines[i - 1].Trim());
            if (i < lines.Length - 1) candidates.Add(lines[i + 1].Trim());

            string? name = null;
            foreach (var candidate in candidates)
            {
                if (candidate.Length > 80) continue;
                var m = NameRegex().Match(candidate);
                if (!m.Success) continue;
                // Unvanin kendisini isim sanmayalim.
                if (TitleKeywords.Any(k => TurkishText.ContainsWord(m.Value, k))) continue;
                name = m.Value;
                break;
            }

            // Isimsiz kayit satis icin kullanilamaz ve menu gurultusunun buyuk kismi
            // isimsiz geldigi icin burada eleniyor.
            if (name is null) continue;

            results.Add((name, CleanTitle(line, matchedTitle)));
        }

        return results
            .GroupBy(r => (r.Item1 + "|" + r.Item2).ToLowerInvariant())
            .Select(g => g.First())
            .Take(20)
            .ToList();
    }

    /// <summary>
    /// Menu bloklari duz metne cevrildiginde tek satirda bitisik baslıklar olarak gelir
    /// ("HakkımızdaTarihçeKurucu..."). Bunlar kisi degil, elenmeli.
    /// </summary>
    private static bool LooksLikeMenu(string line)
    {
        // Kelime arasi bosluk olmadan buyuk harfe gecisler menu birlesmesinin isareti.
        var glued = 0;
        for (var i = 1; i < line.Length; i++)
            if (char.IsLower(line[i - 1]) && char.IsUpper(line[i]))
                glued++;

        return glued >= 3;
    }

    /// <summary>
    /// Unvan satirini kisaltir. Uzun satirlarda eslesen anahtar kelimenin cevresini alir,
    /// boylece Contact.Title alanina paragraf degil unvan yazilir.
    /// </summary>
    private static string CleanTitle(string line, string matchedKeyword)
    {
        if (line.Length <= 60) return line;

        // Anahtar kelime normalize edilmis halde eslesti; konumu da normalize metinde ara.
        // Normalize karakter sayisini degistirmedigi icin indeks ham metinde de gecerli.
        var index = TurkishText.Normalize(line)
            .IndexOf(TurkishText.Normalize(matchedKeyword), StringComparison.Ordinal);
        if (index < 0) return line[..60].Trim();

        var start = Math.Max(0, index - 20);
        var length = Math.Min(60, line.Length - start);
        return line.Substring(start, length).Trim();
    }
}
