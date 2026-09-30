using System.Text.Json;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>AI #9+: rapor yapay zekasi (firma karsilastirma, sektor analizi, ICP onerisi).</summary>
public partial class GeminiAiService : IInsightAi
{
    /// <summary>Semali JSON istegi atar; yanit metnini (JSON) veya hata mesajini doner.</summary>
    private async Task<(string? Json, string? Error)> AskJsonAsync(string systemPrompt, string userText, object schema,
        double temperature, CancellationToken ct)
    {
        var apiKey = await _settings.GetAsync(SettingKeys.GeminiApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey)) return (null, new MissingApiKeyException("Gemini").Message);

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userText } } } },
            generationConfig = new { temperature, responseMimeType = "application/json", responseSchema = schema }
        };

        try
        {
            var url = $"{_options.GeminiEndpoint}/{await ModelAsync(ct)}:generateContent";
            var response = await SendWithRetryAsync(url, JsonSerializer.Serialize(payload), apiKey, ct);
            if (response.Error is not null) return (null, response.Error);

            var json = ExtractText(response.Content!);
            return json is null ? (null, "Gemini yanıtında metin bulunamadı.") : (json, null);
        }
        catch (QuotaExceededException)
        {
            return (null, "Gemini kotası doldu; bir süre sonra tekrar deneyin.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Gemini rapor çağrısı başarısız.");
            return (null, ex.Message);
        }
    }

    // ================= Firma karsilastirma =================

    public async Task<CompareAiResult> CompareCompaniesAsync(BusinessProfile? profile, string companyA, string companyB,
        CancellationToken ct = default)
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                recommended = new { type = "string", @enum = new[] { "A", "B", "esit" } },
                headline = new { type = "string", description = "Tek cümlelik sonuç" },
                summary = new { type = "string", description = "3-5 cümlelik karşılaştırma" },
                aStrengths = StringArray,
                bStrengths = StringArray,
                aRisks = StringArray,
                bRisks = StringArray,
                nextSteps = StringArray
            },
            required = new[] { "recommended", "headline", "summary", "aStrengths", "bStrengths", "aRisks", "bRisks", "nextSteps" }
        };

        var (json, error) = await AskJsonAsync(SellerBlock(profile) + "\n\n" + ComparePrompt,
            $"FİRMA A\n{companyA}\n\nFİRMA B\n{companyB}", schema, 0.2, ct);
        if (error is not null) return new CompareAiResult { Error = error };

        return ParseCompareResult(json!);
    }

    public static CompareAiResult ParseCompareResult(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<CompareAiResult>(json, JsonOptions) ?? new CompareAiResult();
            result.Recommended = result.Recommended?.Trim().ToUpperInvariant() switch
            {
                "A" => "A",
                "B" => "B",
                _ => "esit"
            };
            result.Error = null;
            return result;
        }
        catch (JsonException)
        {
            return new CompareAiResult { Error = "Yapay zekâ yanıtı çözümlenemedi." };
        }
    }

    private const string ComparePrompt = """
        Satış ekibimiz iki potansiyel müşteri firmayı karşılaştırıyor. Her firma için
        uygulamadaki veriler verilecek: lead puanı ve puan kalemleri, şirket profilimize
        uygunluk puanı ve gerekçesi, sektör, NACE, ölçek, üretim, ürünler, ERP/SAP durumu,
        internet ön araştırması (sinyal, özet, finans), kişi ve lead durumu.

        Görevin: yukarıdaki şirket tanımımıza göre hangisinin müşterimiz olma potansiyeli
        daha yüksek, hangisine önce gidilmeli? Verilen şemaya uygun JSON döndür.

        - recommended: öncelik verilecek firma "A" veya "B"; gerçekten fark yoksa "esit".
        - headline: tek cümle sonuç (ör. "A firması ölçeği ve ERP ihtiyacıyla daha güçlü aday").
        - summary: 3-5 cümle, somut verilere dayanarak karşılaştır.
        - aStrengths / bStrengths: her firmanın bizim açımızdan 2-4 güçlü yanı.
        - aRisks / bRisks: her firmanın 1-3 riski veya bilinmeyeni (veri eksikse bunu söyle).
        - nextSteps: satış ekibine 2-4 somut sonraki adım.

        Verilmeyen bilgiyi uydurma; eksik veriyi "bilinmiyor" olarak değerlendir.
        Tüm metinleri Türkçe yaz.
        """;
}
