using System.Text.Json.Serialization;

namespace EgebisLeadFinder.Models;

/// <summary>
/// Puanlama anahtar kelimesi / kriteri: "Biz ne arıyoruz?" tanımından yapay zekânın üretip kullanıcının düzenlediği,
/// puan veren bir ölçüt (ör. "Üretim yapıyor" +15). Firma bu ölçüte uyuyorsa puan dökümünde görünür.
/// Eşleşme: yapay zekâ firma analizinde ölçüt adını işaretlediyse veya terimlerden biri firmanın analizinde geçiyorsa.
/// </summary>
public class ScoringSignal
{
    public const int MaxSignals = 12;
    public const int MinPoints = 3;
    public const int MaxPoints = 20;

    /// <summary>Kısa kriter adı; puan dökümünde "Anahtar kelime: {Name}" olarak görünür.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Eşleşme için aranacak kelimeler (ör. "fabrika", "imalat").</summary>
    [JsonPropertyName("terms")]
    public List<string> Terms { get; set; } = new();

    [JsonPropertyName("points")]
    public int Points { get; set; } = 10;

    /// <summary>Form satırı: "Ad | kelime1, kelime2 | puan". Geçersiz satırlar atlanır.</summary>
    public static List<ScoringSignal> ParseLines(string? text)
    {
        var result = new List<ScoringSignal>();
        foreach (var raw in (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = raw.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) continue;

            var points = 10;
            if (parts.Length >= 3) int.TryParse(parts[2], out points);

            result.Add(new ScoringSignal
            {
                Name = parts[0],
                Terms = parts.Length >= 2
                    ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                    : new List<string>(),
                Points = points == 0 ? 10 : points
            });
        }
        return result;
    }

    public static string FormatLines(IEnumerable<ScoringSignal> signals) =>
        string.Join("\n", signals.Select(s => $"{s.Name} | {string.Join(", ", s.Terms)} | {s.Points}"));

    /// <summary>Ad, terim ve puan sınırlarını uygular; boş/yinelenen ölçütleri atar.</summary>
    public static List<ScoringSignal> Clean(IEnumerable<ScoringSignal>? signals)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ScoringSignal>();
        foreach (var s in signals ?? Enumerable.Empty<ScoringSignal>())
        {
            var name = (s.Name ?? string.Empty).Trim();
            if (name.Length == 0) continue;
            if (name.Length > 80) name = name[..80];
            if (!seen.Add(name)) continue;

            list.Add(new ScoringSignal
            {
                Name = name,
                Terms = (s.Terms ?? new List<string>()).Select(t => t.Trim()).Where(t => t.Length > 1)
                    .Select(t => t.Length > 60 ? t[..60] : t).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList(),
                Points = Math.Clamp(s.Points, MinPoints, MaxPoints)
            });
            if (list.Count >= MaxSignals) break;
        }
        return list;
    }
}
