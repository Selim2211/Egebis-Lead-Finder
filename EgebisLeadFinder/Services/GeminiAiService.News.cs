using System.Text.Json;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>Haber akışı için toplu yapay zekâ zenginleştirmesi girdisi (bir haber).</summary>
public record NewsAiInput(int Index, string Title, string? Snippet, string? CompanyName, bool IsRadar);

/// <summary>Bir haberin yapay zekâ çıktısı: tek cümle özet, kategori, önem (1-5), şehir ve başlıkta geçen firma.</summary>
public record NewsAiResult(int Index, string? Summary, string? Category, int Importance, string? City, string? MentionedCompany, string? Region = null);

public interface INewsAi
{
    /// <summary>Haberleri tek çağrıda özetler ve sınıflar. Hata olursa boş liste döner (haber kural puanıyla görünmeye devam eder).</summary>
    Task<List<NewsAiResult>> EnrichNewsAsync(IReadOnlyList<NewsAiInput> items, CancellationToken ct = default);

    /// <summary>Haberin konusu Türkiye mi yurt dışı mı: index → "tr"/"global". Hata olursa boş sözlük.</summary>
    Task<Dictionary<int, string>> ClassifyRegionsAsync(IReadOnlyList<(int Index, string Text)> items, CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<int, string>());
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
        - region: "tr" = haber Türkiye'deki bir firma, tesis ya da olay hakkında; "global" = yurt dışındaki bir firma/olay hakkında
          (haber Türkçe yazılmış olsa bile; ör. Tesla'nın Almanya fabrikası "global"dir).
        Her girdinin index değerini aynen geri ver.
        """;

    public async Task<List<NewsAiResult>> EnrichNewsAsync(IReadOnlyList<NewsAiInput> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return new List<NewsAiResult>();

        // Haber akisi, Gemini anahtari/lisansi olsa bile Ayarlar'daki secime gore (varsayilan: Groq) calisir.
        using var newsScope = await AiProviders.NewsProviderAsync(_settings, ct) is { } newsProvider ? AiProviders.Force(newsProvider) : null;

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
                            mentionedCompany = new { type = "string" },
                            region = new { type = "string" }
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
                list.Add(new NewsAiResult(index, Str("summary"), Str("category"), Math.Clamp(importance, 1, 5), Str("city"), Str("mentionedCompany"), RegionOf(Str("region"))));
            }
            return list;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Haber yapay zekâ yanıtı okunamadı.");
            return new List<NewsAiResult>();
        }
    }

    private static string? RegionOf(string? value) => Models.NewsRegions.Normalize(value);

    private const string RegionPrompt = """
        Her satırda bir haber var. Haberin KONUSU Türkiye mi yurt dışı mı? Haberin hangi dilde yazıldığına değil, olayın ve firmanın
        nerede olduğuna bak. "tr" = Türkiye'deki bir firma, tesis, kurum ya da olay; "global" = yurt dışındaki bir firma/olay
        (ör. "Tesla Berlin fabrikasını genişletiyor" global; "Vestel Manisa'ya yatırım" tr). Her girdinin index değerini aynen ver.
        """;

    public async Task<Dictionary<int, string>> ClassifyRegionsAsync(IReadOnlyList<(int Index, string Text)> items, CancellationToken ct = default)
    {
        var result = new Dictionary<int, string>();
        if (items.Count == 0) return result;
        using var newsScope = await AiProviders.NewsProviderAsync(_settings, ct) is { } newsProvider ? AiProviders.Force(newsProvider) : null;

        var schema = new
        {
            type = "object",
            properties = new
            {
                items = new
                {
                    type = "array",
                    items = new { type = "object", properties = new { index = new { type = "integer" }, region = new { type = "string" } }, required = new[] { "index", "region" } }
                }
            },
            required = new[] { "items" }
        };
        var user = string.Join("\n", items.Select(i => $"[{i.Index}] {(i.Text.Length > 300 ? i.Text[..300] : i.Text)}"));
        var (json, error) = await AskJsonAsync(RegionPrompt, user, schema, 0.0, ct, localize: false);
        if (error is not null) return result;
        try
        {
            using var doc = JsonDocument.Parse(json!);
            foreach (var e in doc.RootElement.GetProperty("items").EnumerateArray())
                if (e.TryGetProperty("index", out var idx) && idx.TryGetInt32(out var i)
                    && e.TryGetProperty("region", out var r) && RegionOf(r.GetString()) is { } region)
                    result[i] = region;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Haber bölge yanıtı okunamadı.");
        }
        return result;
    }
}
