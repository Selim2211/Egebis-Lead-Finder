using System.Text.Json;
using EgebisLeadFinder.Localization;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>"Biz ne arıyoruz?" tanımından puanlama anahtar kelimeleri (puan veren ölçütler) üretir.</summary>
public interface IScoringSignalAi
{
    Task<ScoringSignalResult> SuggestSignalsAsync(BusinessProfile profile, CancellationToken ct = default);
}

public class ScoringSignalResult
{
    public List<ScoringSignal> Signals { get; init; } = new();
    public string? Error { get; init; }
    public bool Success => Error is null && Signals.Count > 0;
}

public partial class GeminiAiService : IScoringSignalAi
{
    public static string ScoringSignalPrompt(string nameLanguage) => $$"""
        Yukarıdaki şirket tanımına göre, bir firmanın bizim için ne kadar iyi bir müşteri adayı olduğunu gösteren
        6-10 PUANLAMA ÖLÇÜTÜ (anahtar kelime) üret. Her ölçüt, bir firmanın web sitesinde/faaliyetinde görülebilecek somut bir özelliktir
        (ör. satış yaptığımız sektörde faaliyet, bizim ürünümüzü kullanacak bir iş süreci, uygun ölçek, ihtiyaç sinyali).

        Kurallar:
        - name: ölçütün kısa adı (2-5 kelime), {{nameLanguage}} yaz. Örn. "Üretim yapıyor" ya da "Altyapı projeleri".
        - terms: bu ölçütü firmanın sitesinde/ürün listesinde yakalayacak 3-8 kısa kelime veya ifade. Hedef firmaların web sitelerinin
          dilinde (çoğunlukla Türkçe) yaz; gerekirse yaygın İngilizce karşılığını da ekle. Genel/anlamsız kelime (ör. "firma", "hizmet") yazma.
        - points: önem derecesi 5-20. En belirleyici ölçütler 15-20, yardımcı ölçütler 5-10.
        - Rakip veya "müşterimiz olmayanlar" tanımına uyan özellikleri ölçüt yapma.
        - Tanımda olmayan bir şeyi uydurma; şirket tanımına sadık kal.
        """;

    public async Task<ScoringSignalResult> SuggestSignalsAsync(BusinessProfile profile, CancellationToken ct = default)
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                signals = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string", description = "Kısa ölçüt adı" },
                            terms = new { type = "array", items = new { type = "string" }, description = "Eşleşme kelimeleri" },
                            points = new { type = "integer", description = "Önem derecesi, 5-20" }
                        },
                        required = new[] { "name", "terms", "points" }
                    }
                }
            },
            required = new[] { "signals" }
        };

        var language = Loc.IsEnglish ? "İngilizce" : "Türkçe";
        // Kelime dili hedef pazara ait oldugundan arayuz dili istemi etkilemez (localize:false); ad dili acikca verilir.
        var (json, error) = await AskJsonAsync(profile.ToPromptBlock() + "\n\n" + ScoringSignalPrompt(language),
            "Puanlama ölçütlerini üret.", schema, 0.3, ct, localize: false);
        if (error is not null) return new ScoringSignalResult { Error = error };

        try
        {
            using var doc = JsonDocument.Parse(json!);
            var list = new List<ScoringSignal>();
            foreach (var item in doc.RootElement.GetProperty("signals").EnumerateArray())
            {
                list.Add(new ScoringSignal
                {
                    Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                    Terms = item.TryGetProperty("terms", out var t) && t.ValueKind == JsonValueKind.Array
                        ? t.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                        : new List<string>(),
                    Points = item.TryGetProperty("points", out var p) && p.TryGetInt32(out var pv) ? pv : 10
                });
            }

            var cleaned = ScoringSignal.Clean(list);
            return cleaned.Count == 0
                ? new ScoringSignalResult { Error = "Yapay zekâ puanlama ölçütü üretemedi." }
                : new ScoringSignalResult { Signals = cleaned };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new ScoringSignalResult { Error = "Ölçüt yanıtı okunamadı." };
        }
    }
}

/// <summary>
/// Profilde puanlama anahtar kelimesi yoksa yapay zekâya ürettirip profile kaydeder; elle girilen/düzenlenen listeye dokunmaz.
/// </summary>
public class ProfileSignalService
{
    private readonly BusinessProfileService _profiles;
    private readonly IScoringSignalAi _ai;

    public ProfileSignalService(BusinessProfileService profiles, IScoringSignalAi ai)
    {
        _profiles = profiles;
        _ai = ai;
    }

    /// <summary>Profil tanımlı ve ölçüt listesi boşsa üretir ve kaydeder. Hata olursa null yerine hata metni döner; üretilen sayıyı verir.</summary>
    public async Task<(int Created, string? Error)> EnsureAsync(string? updatedBy, CancellationToken ct = default)
    {
        var profile = await _profiles.GetAsync(ct);
        if (!profile.IsConfigured || profile.ScoringSignals.Count > 0) return (0, null);

        var result = await _ai.SuggestSignalsAsync(profile, ct);
        if (!result.Success) return (0, result.Error);

        profile.ScoringSignals = result.Signals;
        await _profiles.SaveAsync(profile, targetTitles: null, updatedBy ?? profile.UpdatedBy, ct);
        return (result.Signals.Count, null);
    }
}
