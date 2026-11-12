using System.Text.Json;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>Haber akışı için toplu yapay zekâ zenginleştirmesi girdisi (bir haber).</summary>
public record NewsAiInput(int Index, string Title, string? Snippet, string? CompanyName, bool IsRadar);

/// <summary>Bir haberin yapay zekâ çıktısı: tek cümle özet, kategori, önem (1-5), şehir ve başlıkta geçen firma.</summary>
public record NewsAiResult(int Index, string? Summary, string? Category, int Importance, string? City, string? MentionedCompany);

public interface INewsAi
{
    /// <summary>Haberleri tek çağrıda özetler ve sınıflar. Hata olursa boş liste döner (haber kural puanıyla görünmeye devam eder).</summary>
    Task<List<NewsAiResult>> EnrichNewsAsync(IReadOnlyList<NewsAiInput> items, CancellationToken ct = default);
}

public partial class GeminiAiService : INewsAi
{
    private const string NewsPrompt = """
        Sen bir iş geliştirme analistisin. Aşağıdaki haber başlıklarını, bir B2B satış ekibinin fırsat radarı için değerlendir.
        Satış ekibi, yeni fabrika/üretim tesisi, kapasite artışı, yatırım, büyük sipariş, yeni yönetici gibi "hemen iletişime geçilecek" haberleri arıyor.

        Her haber için:
        - summary: Türkçe, TEK cümle, en fazla 140 karakter; haberin satış açısından anlamını yaz. Haberde olmayan bilgiyi uydurma.
        - category: yalnızca şunlardan biri: yatirim, risk, finans, yonetim, teknoloji, genel.
        - importance: 1-5. 5 = yeni fabrika/tesis/büyük yatırım gibi acil fırsat; 4 = güçlü fırsat veya ciddi risk; 3 = ilgili gelişme;
          2 = zayıf ilgi; 1 = satış için ilgisiz (spor, magazin, başka sektör, genel ekonomi).
        - city: haberde geçen Türkiye şehri (yoksa boş bırak).
        - mentionedCompany: haberin asıl öznesi olan firma adı (yoksa boş bırak).
        Her girdinin index değerini aynen geri ver.
        """;

    public async Task<List<NewsAiResult>> EnrichNewsAsync(IReadOnlyList<NewsAiInput> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return new List<NewsAiResult>();

        var schema = new
        {
            type = "object",
            properties = new
            {
                items = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            index = new { type = "integer" },
                            summary = new { type = "string" },
                            category = new { type = "string" },
                            importance = new { type = "integer" },
                            city = new { type = "string" },
                            mentionedCompany = new { type = "string" }
                        },
                        required = new[] { "index", "summary", "category", "importance" }
                    }
                }
            },
            required = new[] { "items" }
        };

        var user = string.Join("\n", items.Select(i =>
            $"[{i.Index}] {(i.IsRadar ? "(radar)" : $"(takip: {i.CompanyName})")} {i.Title}" +
            (string.IsNullOrWhiteSpace(i.Snippet) ? "" : $" — {(i.Snippet!.Length > 300 ? i.Snippet[..300] : i.Snippet)}")));

        var (json, error) = await AskJsonAsync(NewsPrompt, user, schema, 0.2, ct, localize: false);
        if (error is not null) return new List<NewsAiResult>();

        try
        {
            using var doc = JsonDocument.Parse(json!);
            var list = new List<NewsAiResult>();
            foreach (var e in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                if (!e.TryGetProperty("index", out var idx) || !idx.TryGetInt32(out var index)) continue;
                string? Str(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var importance = e.TryGetProperty("importance", out var imp) && imp.TryGetInt32(out var iv) ? iv : 1;
                list.Add(new NewsAiResult(index, Str("summary"), Str("category"), Math.Clamp(importance, 1, 5), Str("city"), Str("mentionedCompany")));
            }
            return list;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Haber yapay zekâ yanıtı okunamadı.");
            return new List<NewsAiResult>();
        }
    }
}
