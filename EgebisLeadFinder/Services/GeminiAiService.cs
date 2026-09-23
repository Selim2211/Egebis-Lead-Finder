using System.Text.Json;
using System.Text.Json.Serialization;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <summary>
/// AI #1: firma analizi. Gemini'ye responseSchema verilerek JSON ciktisi sema ile zorlanir,
/// boylece serbest metin ayristirma riski ortadan kalkar.
/// </summary>
public class GeminiAiService : IAiService, ICompanyRatingAi
{
    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly ISettingsService _settings;
    private readonly IApiUsageTracker _usage;
    private readonly ILogger<GeminiAiService> _logger;

    /// <summary>Ayarlar ekranindaki gunluk/haftalik/aylik kullanim sayaci bu saglayici adiyla tutulur.</summary>
    public const string UsageProvider = "Gemini";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GeminiAiService(
        HttpClient http,
        IOptions<AiOptions> options,
        ISettingsService settings,
        IApiUsageTracker usage,
        ILogger<GeminiAiService> logger)
    {
        _http = http;
        _options = options.Value;
        _settings = settings;
        _usage = usage;
        _logger = logger;
    }

    /// <summary>Ayarlar ekraninda secilen model; secim yoksa appsettings Ai:GeminiModel.</summary>
    private async Task<string> ModelAsync(CancellationToken ct)
    {
        var stored = (await _settings.GetAsync(SettingKeys.GeminiModel, ct))?.Trim();
        return GeminiModelCatalog.IsValidModelId(stored) ? stored! : _options.GeminiModel;
    }

    public async Task<AiAnalysisResult> AnalyzeCompanyAsync(string siteText, CancellationToken ct = default)
    {
        // Anahtar yalnizca Ayarlar ekranindan okunur (bkz. SettingKeys.IsSettingsOnly).
        var apiKey = await _settings.GetAsync(SettingKeys.GeminiApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            return AiAnalysisResult.Failed(new MissingApiKeyException("Gemini").Message);

        if (string.IsNullOrWhiteSpace(siteText))
            return AiAnalysisResult.Failed("Analiz edilecek metin boş.");

        var input = siteText.Length > _options.MaxInputChars
            ? siteText[.._options.MaxInputChars]
            : siteText;

        var url = $"{_options.GeminiEndpoint}/{await ModelAsync(ct)}:generateContent";

        var payload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = SystemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = "Firma web sitesi metni:\n\n" + input } }
                }
            },
            generationConfig = new
            {
                temperature = 0.1,
                responseMimeType = "application/json",
                responseSchema = ResponseSchema
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload);

        try
        {
            var body = await SendWithRetryAsync(url, payloadJson, apiKey, ct);
            if (body.Error is not null) return AiAnalysisResult.Failed(body.Error);

            var json = ExtractText(body.Content!);
            if (json is null)
                return AiAnalysisResult.Failed("Gemini yanıtında metin bulunamadı.");

            var analysis = JsonSerializer.Deserialize<CompanyAnalysis>(json, JsonOptions);
            if (analysis is null)
                return AiAnalysisResult.Failed("Gemini JSON çıktısı çözümlenemedi.");

            return new AiAnalysisResult { Analysis = analysis, RawJson = json };
        }
        catch (QuotaExceededException)
        {
            // Kota hatasi tek firmanin sorunu degil: cagirana kadar cikar, arama durur.
            throw;
        }
        catch (Exception ex)
        {
            // AI patlarsa akis durmaz; firma AI analizi olmadan puanlanir.
            _logger.LogWarning(ex, "Gemini çağrısı başarısız.");
            return AiAnalysisResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Gemini'nin ucretsiz katmani dakikalik istek limiti uygular ve limit asilinca
    /// 429 doner. Bu gecici bir durumdur, artan bekleme ile tekrar denenir.
    /// </summary>
    private async Task<(string? Content, string? Error)> SendWithRetryAsync(
        string url, string payloadJson, string apiKey, CancellationToken ct)
    {
        // Gemini ucretsiz katmaninda "model su an yogun talep altinda" (503) hatasi
        // dakikalarca surebilir; 4 deneme (~35 sn) cogu zaman yetersiz kaliyordu.
        const int maxAttempts = 6;
        var delay = TimeSpan.FromMilliseconds(_options.RetryBaseDelayMs);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                await _usage.IncrementAsync(UsageProvider, ct);
                return (body, null);
            }

            var status = (int)response.StatusCode;

            // Gunluk/aylik kota bittiyse beklemek ise yaramaz: her firma icin 6 kez
            // ~35 sn bosa beklemek yerine akisi hemen durduruyoruz.
            if (status == 429 && QuotaExceededException.LooksLikeQuota(body))
            {
                _logger.LogWarning("Gemini kotası doldu: {Body}", Truncate(body, 300));
                throw new QuotaExceededException("Gemini");
            }

            var retryable = status == 429 || status >= 500;

            if (!retryable || attempt == maxAttempts)
            {
                _logger.LogWarning("Gemini hatası {Status}: {Body}", status, Truncate(body, 400));
                return (null, $"Gemini API {status}: {Truncate(body, 200)}");
            }

            _logger.LogInformation(
                "Gemini {Status} döndü, {Delay} sn sonra tekrar denenecek ({Attempt}/{Max}).",
                status, delay.TotalSeconds, attempt, maxAttempts);

            await Task.Delay(delay, ct);
            delay *= 2;
        }

        return (null, "Gemini API yanıt vermedi.");
    }

    /// <summary>Gemini yanit zarfindan ilk parcanin metnini cikarir.</summary>
    private static string? ExtractText(string body)
    {
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0)
            return null;

        if (!candidates[0].TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.GetArrayLength() == 0)
            return null;

        return parts[0].TryGetProperty("text", out var text) ? text.GetString() : null;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private const string SystemPrompt = """
        Egebis Bilişim için potansiyel müşteri analizi yapıyorsun.

        Egebis, üretim yapan firmalara SAP danışmanlığı, SAP entegrasyonu,
        MES/üretim takip ve özel yazılım hizmeti veriyor.
        Bu nedenle EN DEĞERLİ hedef: SAP kullanan üretici firmalar/fabrikalar.

        Sana bir firmanın web sitesinden alınan düz metin verilecek.
        Metne dayanarak firmayı değerlendir ve verilen şemaya uygun JSON döndür.

        SAP tespiti kuralları (sap alanı):
        - "yes": Metinde firmanın SAP kullandığına dair somut kanıt var.
          Örnekler: "SAP ERP kullanıyoruz", "SAP S/4HANA'ya geçtik",
          iş ilanında "SAP MM modülü deneyimi", "SAP kullanıcısı aranıyor",
          SAP referans/sertifika görselleri, SAP entegrasyon duyurusu.
        - "likely": Doğrudan SAP geçmiyor ama güçlü işaret var; örneğin
          kurumsal ERP kullanımından söz ediliyor, çok uluslu grup şirketi,
          büyük ölçekli üretim tesisi.
        - "no": Metin başka bir ERP'yi (Logo, Netsis, Nebim, Mikro, Canias)
          açıkça belirtiyor.
        - "unknown": Metinde bu konuda hiçbir bilgi yok.

        sapEvidence alanına, "yes" veya "likely" dediysen metinden bunu
        destekleyen kısa alıntıyı yaz. Kanıt yoksa boş bırak.
        Metinde olmayan bilgiyi asla uydurma.

        sapVendor alanı: firma SAP danışmanlığı, SAP entegrasyonu veya SAP
        eklentisi SATIYOR mu? Bunlar Egebis'in rakibidir, müşterisi değil.
        Yazılım evi, ERP danışmanlık şirketi, SAP iş ortağı ise true.
        SAP'ı kendi üretimi için KULLANAN firma ise false.

        potential alanı: firma Egebis için gerçek bir satış hedefi mi?
        Üretim yapan ve SAP kullanan/kullanma ihtimali olan firmalar için true.
        Aşağıdakiler için mutlaka false ver:
        - haber sitesi, gazete, haber ajansı
        - iş ilanı / kariyer platformu (bir firmanın kendi kariyer sayfası hariç)
        - yazılım evi, SAP danışmanlık şirketi, bilişim hizmet sağlayıcı
        - bayi, perakendeci, e-ticaret sitesi
        - dernek, oda, vakıf, kamu kurumu, üniversite
        - firma rehberi, blog, forum

        Dikkat: metin bir firma HAKKINDA haber veya o firma için verilmiş bir
        iş ilanı olabilir. Bu durumda sitenin sahibi haber/ilan sitesidir;
        haberde adı geçen firma değil. Sitenin sahibini değerlendir.

        companyName alanı: sitenin sahibi olan firmanın adı.
        Haber başlığı veya ilan başlığı değil, firmanın kendi ticari adı.

        recommendedTemplate alanı şu değerlerden biri olmalı:
        - "SAP_ENTEGRASYON": SAP kullanıyor, entegrasyon ihtiyacı olabilir
        - "SAP": SAP kullanıyor veya geçiş ihtimali var, genel danışmanlık
        - "MES": Üretim yapıyor, üretim takip/izleme ihtiyacı öne çıkıyor
        - "URETIM_YAZILIMI": Üretici ama SAP izi yok
        - "GENEL": Diğer tüm durumlar

        Tüm metin alanlarını Türkçe yaz.
        """;

    /// <summary>
    /// Gemini responseSchema'si. CompanyAnalysis sinifiyla birebir eslesmelidir.
    /// </summary>
    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            companyName = new { type = "string", description = "Sitenin sahibi firmanın ticari adı" },
            industry = new { type = "string", description = "Firmanın sektörü" },
            manufacturer = new { type = "boolean", description = "Üretim yapıyor mu" },
            products = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Ana ürünler"
            },
            sap = new
            {
                type = "string",
                @enum = new[] { "yes", "likely", "no", "unknown" }
            },
            sapEvidence = new { type = "string", description = "SAP kullanımına dair metinden alıntı" },
            sapVendor = new { type = "boolean", description = "SAP hizmeti satan firma mı (rakip)" },
            employeeSizeHint = new { type = "string", description = "Çalışan sayısı ipucu, yoksa boş" },
            potential = new { type = "boolean", description = "Egebis için gerçek satış hedefi mi" },
            reason = new { type = "string", description = "Kısa gerekçe" },
            recommendedTemplate = new
            {
                type = "string",
                @enum = new[] { "SAP_ENTEGRASYON", "SAP", "MES", "URETIM_YAZILIMI", "GENEL" }
            }
        },
        required = new[]
        {
            "companyName", "industry", "manufacturer", "sap", "sapVendor",
            "potential", "reason", "recommendedTemplate"
        }
    };

    // ================= AI #3: Firma on arastirma / rating =================

    /// <summary>
    /// Toplanan kaynak parcalarindan yapisal firma degerlendirmesi uretir.
    /// AI yalnizca verilen metinlerden yazar; nihai sinyali CompanyRatingEvaluator belirler.
    /// </summary>
    public async Task<CompanyRatingAiResult> RateCompanyAsync(CompanyRatingInput input, CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync(SettingKeys.GeminiApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            return CompanyRatingAiResult.Failed(new MissingApiKeyException("Gemini").Message);

        if (input.Snippets.Count == 0)
            return CompanyRatingAiResult.Failed("Değerlendirilecek kaynak bilgisi yok.");

        var sources = string.Join("\n\n", input.Snippets.Select((s, i) =>
            $"[{i + 1}] ({s.Kind}) {s.SourceUrl}\n{s.Text}"));

        if (sources.Length > _options.MaxInputChars)
            sources = sources[.._options.MaxInputChars];

        var analysisNote = input.Analysis is null
            ? "(firma analizi yok)"
            : $"Sektör: {input.Analysis.Industry}; Üretici: {input.Analysis.Manufacturer}; " +
              $"Ürünler: {string.Join(", ", input.Analysis.Products)}";

        var userText =
            $"FİRMA: {input.Company.Name}\n" +
            $"Web: {input.Company.Website}\n" +
            $"Şehir: {input.Company.City}\n" +
            $"Mevcut analiz: {analysisNote}\n\n" +
            $"KAYNAKLAR (yalnızca bunları kullan):\n{sources}";

        var url = $"{_options.GeminiEndpoint}/{await ModelAsync(ct)}:generateContent";

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = RatingSystemPrompt } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = userText } } }
            },
            generationConfig = new
            {
                temperature = 0.1,
                responseMimeType = "application/json",
                responseSchema = RatingResponseSchema
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload);

        try
        {
            var body = await SendWithRetryAsync(url, payloadJson, apiKey, ct);
            if (body.Error is not null) return CompanyRatingAiResult.Failed(body.Error);

            var json = ExtractText(body.Content!);
            if (json is null)
                return CompanyRatingAiResult.Failed("Gemini yanıtında metin bulunamadı.");

            var rating = JsonSerializer.Deserialize<CompanyRating>(json, JsonOptions);
            if (rating is null)
                return CompanyRatingAiResult.Failed("Gemini JSON çıktısı çözümlenemedi.");

            return new CompanyRatingAiResult { Rating = rating, RawJson = json };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini rating çağrısı başarısız.");
            return CompanyRatingAiResult.Failed(ex.Message);
        }
    }

    private const string RatingSystemPrompt = """
        Egebis Bilişim için bir potansiyel müşterinin (firmanın) ticari ve finansal
        sağlık durumunu değerlendiriyorsun. Bu değerlendirme, firmanın Egebis'e
        uygunluğundan (SAP kullanımı vb.) AYRI bir eksendir.

        Sana firma hakkında internetten toplanmış kaynak parçaları (haber başlıkları,
        arama sonucu özetleri, KAP bildirimleri) verilecek. SADECE bu parçalardan
        yararlanarak verilen şemaya uygun JSON döndür.

        Kesin kurallar:
        - Metinde olmayan hiçbir bilgiyi uydurma. Bilgi yoksa alanı boş bırak veya
          "bilinmiyor" yaz.
        - Her risk sinyali (riskSignals) için sourceUrl alanına o bilgiyi veren
          kaynağın linkini koy. Kaynağı olmayan risk iddiası yazma.
        - riskSignals.severity: konkordato, iflas, haciz, tasfiye, el koyma =>
          "yuksek". Dava, icra takibi, ödeme gecikmesi haberi => "orta".
          Belirsiz/söylenti => "dusuk".
        - financialSource: rakam KAP bildiriminden geliyorsa "KAP", haberden
          geliyorsa "haber", hiç rakam yoksa "yok".
        - signal alanı senin ÖNERİN: net risk yoksa ve firma köklü/aktif görünüyorsa
          "guclu"; veri az veya karışıksa "incelenmeli"; doğrulanmış ciddi risk
          varsa "riskli". Nihai kararı sistem verecek.
        - customers/suppliers/projects/growthSignals: kaynaklarda açıkça geçen
          isimleri/olayları kısa madde olarak yaz. Yoksa boş dizi.

        Tüm metin alanlarını Türkçe yaz.
        """;

    /// <summary>Rating responseSchema'si. CompanyRating sinifiyla birebir eslesmelidir.</summary>
    private static readonly object RatingResponseSchema = new
    {
        type = "object",
        properties = new
        {
            signal = new { type = "string", @enum = new[] { "guclu", "incelenmeli", "riskli" } },
            summary = new { type = "string", description = "1-2 cümlelik Türkçe özet" },
            foundingInfo = new { type = "string", description = "Kuruluş yılı, merkez, faaliyet alanı" },
            scaleInfo = new { type = "string", description = "Çalışan sayısı, şube/fabrika" },
            financialInfo = new { type = "string", description = "Ciro/kâr/sermaye bilgisi, yoksa boş" },
            financialSource = new { type = "string", @enum = new[] { "KAP", "haber", "yok" } },
            owners = new { type = "string", description = "Ortaklık yapısı ve önemli yöneticiler" },
            customers = new { type = "array", items = new { type = "string" } },
            suppliers = new { type = "array", items = new { type = "string" } },
            projects = new { type = "array", items = new { type = "string" } },
            growthSignals = new { type = "array", items = new { type = "string" } },
            riskSignals = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        text = new { type = "string" },
                        severity = new { type = "string", @enum = new[] { "yuksek", "orta", "dusuk" } },
                        sourceUrl = new { type = "string" }
                    },
                    required = new[] { "text", "severity" }
                }
            }
        },
        required = new[] { "signal", "summary", "financialSource" }
    };
}
