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
public partial class GeminiAiService : IAiService, ICompanyRatingAi, IEmailWriterAi, INaceClassifierAi, IBusinessProfileAi, ISearchPlannerAi
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

    /// <summary>"Biz ne arıyoruz?" sirket profili; bossa talimatlar eski Egebis tanimini kullanir.</summary>
    private async Task<BusinessProfile> ProfileAsync(CancellationToken ct) =>
        BusinessProfileService.Parse(await _settings.GetAsync(SettingKeys.BusinessProfile, ct));

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
                parts = new[] { new { text = AnalysisPrompt(await ProfileAsync(ct)) } }
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
        string url, string payloadJson, string apiKey, CancellationToken ct, TimeSpan? timeout = null)
    {
        var attemptTimeout = timeout ?? TimeSpan.FromSeconds(_options.TimeoutSeconds);
        // Gemini ucretsiz katmaninda "model su an yogun talep altinda" (503) hatasi
        // dakikalarca surebilir; 4 deneme (~35 sn) cogu zaman yetersiz kaliyordu.
        const int maxAttempts = 6;
        var delay = TimeSpan.FromMilliseconds(_options.RetryBaseDelayMs);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(attemptTimeout);

            HttpResponseMessage response;
            string body;
            try
            {
                response = await _http.SendAsync(request, attemptCts.Token);
                body = await response.Content.ReadAsStringAsync(attemptCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Gemini {attemptTimeout.TotalSeconds:0} sn içinde yanıt vermedi.");
            }

            using var responseScope = response;

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
                await _usage.RecordErrorAsync(UsageProvider, "Kota doldu (HTTP 429)", ct);
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

    private const string LegacyAnalysisHeader = """
        Egebis Bilişim için potansiyel müşteri analizi yapıyorsun.

        Egebis, üretim yapan firmalara SAP danışmanlığı, SAP entegrasyonu,
        MES/üretim takip ve özel yazılım hizmeti veriyor.
        Bu nedenle EN DEĞERLİ hedef: SAP kullanan üretici firmalar/fabrikalar.
        """;

    private const string AnalysisIntro = """
        Sana bir firmanın web sitesinden alınan düz metin verilecek.
        Metne dayanarak firmayı değerlendir ve verilen şemaya uygun JSON döndür.
        """;

    private const string NoSapRule = """
        sap alanı bu çalışmada kullanılmıyor: "unknown" yaz, sapEvidence alanını boş bırak.
        manufacturer alanı: firma kendi üretimini yapıyorsa true.
        Metinde olmayan bilgiyi asla uydurma.
        """;

    private const string SapRules = """
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
        """;

    private const string LegacyAnalysisTarget = """
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
        """;

    private const string ProfileAnalysisTarget = """
        sapVendor alanı (RAKİP): firma, bizim sattığımız ürün/hizmetle aynı veya
        benzer bir şeyi başkalarına SATIYOR mu? Yukarıdaki rakip tanımına uyuyorsa
        true. Bizim ürün/hizmetimizi kendi işi için KULLANABİLECEK firma ise false.

        potential alanı: firma bizim için gerçek bir satış hedefi mi?
        Yukarıdaki İDEAL MÜŞTERİMİZ veya HEDEF SEGMENTLER tanımına uyan ve sattığımız
        ürün/hizmete ihtiyaç duyabilecek firmalar için true.
        "Müşterimiz olmayanlar" tanımına uyan firmalar ve rakipler için false.
        Şirket tanımında açıkça hedef olarak yazmıyorsa şunlar için de false ver:
        - haber sitesi, gazete, haber ajansı
        - iş ilanı / kariyer platformu (bir firmanın kendi kariyer sayfası hariç)
        - dernek, oda, vakıf, kamu kurumu, üniversite
        - firma rehberi, blog, forum, pazaryeri

        reason alanı: 1-2 cümle; firma neden hedefimize uyuyor ya da uymuyor. Uyuyorsa
        sattığımız hangi ürün/hizmetle ilgili olabileceğini yaz.

        fitScore alanı: firmanın bizim için ne kadar iyi bir müşteri adayı olduğu, 0-100.
        - 80-100: ideal müşteri tanımına ve bir hedef segmente net uyuyor, ürünümüze
          açık ihtiyacı var.
        - 50-79: büyük ölçüde uyuyor ama bir eksik var (büyüklük, sektör kenarı, belirsiz ihtiyaç).
        - 20-49: zayıf uyum; ancak dolaylı bir fırsat olabilir.
        - 0-19: hedef değil (potential=false olan firmalar bu aralıkta olmalı).
        Metinde olmayan bilgiyi varsayma; bilgi azsa puanı düşük tut.

        segment alanı: firma yukarıdaki HEDEF SEGMENTLER'den birine uyuyorsa o segmentin
        adını aynen yaz; hiçbirine uymuyorsa boş bırak.
        """;

    private const string AnalysisTail = """
        Dikkat: metin bir firma HAKKINDA haber veya o firma için verilmiş bir
        iş ilanı olabilir. Bu durumda sitenin sahibi haber/ilan sitesidir;
        haberde adı geçen firma değil. Sitenin sahibini değerlendir.

        companyName alanı: sitenin sahibi olan firmanın adı.
        Haber başlığı veya ilan başlığı değil, firmanın kendi ticari adı.

        naceCode alanı: firmanın ana faaliyetine en uygun NACE Rev.2 sınıf kodu,
        "22.19" biçiminde (bölüm.sınıf). Sınıftan emin değilsen yalnızca bölüm
        kodunu yaz ("22"). Faaliyet belirsizse boş bırak.

        """;

    private const string LegacyTemplateRule = """
        recommendedTemplate alanı şu değerlerden biri olmalı:
        - "SAP_ENTEGRASYON": SAP kullanıyor, entegrasyon ihtiyacı olabilir
        - "SAP": SAP kullanıyor veya geçiş ihtimali var, genel danışmanlık
        - "MES": Üretim yapıyor, üretim takip/izleme ihtiyacı öne çıkıyor
        - "URETIM_YAZILIMI": Üretici ama SAP izi yok
        - "GENEL": Diğer tüm durumlar

        Tüm metin alanlarını Türkçe yaz.
        """;

    /// <summary>Profil modunda e-posta sablonu firmanin segmentinden secilir; yapay zeka "GENEL" der.</summary>
    private const string ProfileTemplateRule = """
        recommendedTemplate alanına "GENEL" yaz (şablon firmanın segmentine göre seçilir).

        Tüm metin alanlarını Türkçe yaz.
        """;

    /// <summary>
    /// Firma analizi talimati. Sirket profili ("Biz ne arıyoruz?") doluysa hedef ve rakip tanimi
    /// profilden gelir; bossa eski Egebis tanimi aynen kullanilir.
    /// </summary>
    public static string AnalysisPrompt(BusinessProfile? profile) =>
        profile is { IsConfigured: true }
            ? string.Join("\n\n", profile.ToPromptBlock(), AnalysisIntro, profile.MentionsSap ? SapRules : NoSapRule,
                ProfileAnalysisTarget, AnalysisTail, ProfileTemplateRule)
            : string.Join("\n\n", LegacyAnalysisHeader, AnalysisIntro, SapRules, LegacyAnalysisTarget, AnalysisTail, LegacyTemplateRule);

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
            naceCode = new { type = "string", description = "NACE Rev.2 kodu, ör. 22.19" },
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
            sapVendor = new { type = "boolean", description = "Rakip mi (bizim sattığımızı satan firma)" },
            employeeSizeHint = new { type = "string", description = "Çalışan sayısı ipucu, yoksa boş" },
            potential = new { type = "boolean", description = "Bizim için gerçek satış hedefi mi" },
            reason = new { type = "string", description = "Kısa gerekçe" },
            recommendedTemplate = new
            {
                type = "string",
                @enum = new[] { "SAP_ENTEGRASYON", "SAP", "MES", "URETIM_YAZILIMI", "GENEL" }
            },
            fitScore = new { type = "integer", description = "Bizim için müşteri adayı olarak uygunluk, 0-100" },
            segment = new { type = "string", description = "Uyduğu hedef segmentin adı veya boş" }
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

        var sources = BuildSourcesBlock(input.Snippets, input.MaxInputChars);

        var analysisNote = input.Analysis is null
            ? "(firma analizi yok)"
            : $"Sektör: {input.Analysis.Industry}; Üretici: {input.Analysis.Manufacturer}; " +
              $"SAP: {input.Analysis.Sap}; Ürünler: {string.Join(", ", input.Analysis.Products)}";

        var userText =
            $"FİRMA: {input.Company.Name}\n" +
            $"Web: {input.Company.Website}\n" +
            $"Şehir: {input.Company.City}\n" +
            $"Mevcut analiz: {analysisNote}\n\n" +
            $"KAYNAKLAR (yalnızca bunları kullan):\n{sources}";

        var url = $"{_options.GeminiEndpoint}/{await ModelAsync(ct)}:generateContent";

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = RatingPrompt(await ProfileAsync(ct)) } } },
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
            var body = await SendWithRetryAsync(url, payloadJson, apiKey, ct, input.Timeout);
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

    /// <summary>
    /// Kaynaklari onem sirasina gore dizer ve sinira kadar ekler: kesilme olursa
    /// risk/finans/site yerine siradan haberler duser. Parca ortadan bolunmez.
    /// </summary>
    public static string BuildSourcesBlock(IReadOnlyList<IntelSnippet> snippets, int maxChars)
    {
        var ordered = snippets
            .Select((s, i) => (Snippet: s, Index: i))
            .OrderBy(x => SourcePriority(x.Snippet.Kind))
            .ThenBy(x => x.Index);

        var sb = new System.Text.StringBuilder();
        var n = 0;
        foreach (var (s, _) in ordered)
        {
            var date = string.IsNullOrWhiteSpace(s.Date) ? "" : $" [{s.Date}]";
            var block = $"[{++n}] ({s.Kind}){date} {s.SourceUrl}\n{s.Text}\n\n";
            if (sb.Length + block.Length > maxChars)
            {
                if (sb.Length == 0) sb.Append(block[..Math.Min(block.Length, maxChars)]);
                continue; // daha kisa bir sonraki parca hala sigabilir
            }
            sb.Append(block);
        }

        return sb.ToString().TrimEnd();
    }

    private static int SourcePriority(IntelKind kind) => kind switch
    {
        IntelKind.Risk => 0,
        IntelKind.Finansal => 1,
        IntelKind.Site => 2,
        IntelKind.Teknoloji => 3,
        IntelKind.Yonetim => 4,
        IntelKind.Buyume => 5,
        IntelKind.Kayit => 6,
        IntelKind.Haber => 7,
        _ => 8
    };

    // ================= AI #4: Kisiye ozel e-posta =================

    public async Task<EmailDraftAiResult> WriteEmailAsync(EmailDraftInput input, CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync(SettingKeys.GeminiApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            return EmailDraftAiResult.Failed(new MissingApiKeyException("Gemini").Message);

        var profile = await ProfileAsync(ct);
        var url = $"{_options.GeminiEndpoint}/{await ModelAsync(ct)}:generateContent";
        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = EmailPrompt(profile) } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = BuildEmailBrief(input, profile) } } } },
            generationConfig = new
            {
                temperature = 0.6,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        subject = new { type = "string", description = "En fazla 70 karakter" },
                        body = new { type = "string", description = "Düz metin; paragraflar arasında boş satır" }
                    },
                    required = new[] { "subject", "body" }
                }
            }
        };

        try
        {
            var response = await SendWithRetryAsync(url, JsonSerializer.Serialize(payload), apiKey, ct);
            if (response.Error is not null) return EmailDraftAiResult.Failed(response.Error);

            var json = ExtractText(response.Content!);
            if (json is null) return EmailDraftAiResult.Failed("Gemini yanıtında metin bulunamadı.");

            using var doc = JsonDocument.Parse(json);
            return new EmailDraftAiResult
            {
                Subject = doc.RootElement.TryGetProperty("subject", out var s) ? s.GetString() : null,
                Body = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() : null
            };
        }
        catch (QuotaExceededException)
        {
            return EmailDraftAiResult.Failed("Gemini kotası doldu; bir süre sonra tekrar deneyin.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini e-posta taslağı başarısız.");
            return EmailDraftAiResult.Failed(ex.Message);
        }
    }

    /// <summary>AI'a verilen bilgi notu: yalnizca dogrulanmis bilgiler, kaynak listesiyle.</summary>
    public static string BuildEmailBrief(EmailDraftInput input, BusinessProfile? profile = null)
    {
        var sb = new System.Text.StringBuilder();
        var c = input.Company;
        var r = input.Rating;
        var a = input.Analysis;

        sb.AppendLine($"DİL: {input.Language}");
        sb.AppendLine($"MAİL TÜRÜ: {(input.IsFollowUp ? "takip (daha önce mail atıldı, cevap gelmedi)" : "ilk temas")}");
        var senderCompany = profile is { IsConfigured: true } ? profile.DisplayName : "Egebis Bilişim";
        sb.AppendLine($"GÖNDEREN: {input.SenderName} ({senderCompany})");
        sb.AppendLine();
        sb.AppendLine($"ALICI: {input.Contact?.Name ?? "(isim bilinmiyor — 'Sayın Yetkili' kullan)"}");
        if (!string.IsNullOrWhiteSpace(input.Contact?.Title)) sb.AppendLine($"Ünvan: {input.Contact.Title}");
        sb.AppendLine();
        sb.AppendLine($"FİRMA: {c.Name} ({c.City}{(string.IsNullOrWhiteSpace(c.Country) ? "" : ", " + c.Country)})");
        if (a is not null)
        {
            var sapRelevant = profile is not { IsConfigured: true } || profile.MentionsSap;
            sb.AppendLine($"Sektör: {a.Industry}; Üretici: {(a.Manufacturer ? "evet" : "hayır")}" + (sapRelevant ? $"; SAP: {a.Sap}" : ""));
            if (sapRelevant && !string.IsNullOrWhiteSpace(a.SapEvidence)) sb.AppendLine($"SAP kanıtı: {a.SapEvidence}");
            if (!string.IsNullOrWhiteSpace(c.FitSegment)) sb.AppendLine($"Uyduğu hedef segmentimiz: {c.FitSegment}");
            if (!string.IsNullOrWhiteSpace(a.Reason)) sb.AppendLine($"Neden uygun: {a.Reason}");
            if (a.Products.Count > 0) sb.AppendLine($"Ürünler: {string.Join(", ", a.Products.Take(6))}");
        }

        if (r is not null)
        {
            if (!string.IsNullOrWhiteSpace(r.Summary)) sb.AppendLine($"Firma özeti: {r.Summary}");
            if (!string.IsNullOrWhiteSpace(r.SalesApproach)) sb.AppendLine($"Satış önerisi: {r.SalesApproach}");
            foreach (var o in r.Opportunities.Take(4)) sb.AppendLine($"Fırsat: {o.Text} — {o.Reason}");
            if (r.Technology is { IsEmpty: false } t)
                sb.AppendLine($"Teknoloji: ERP {t.Erp}; {string.Join(", ", t.Software.Concat(t.DigitalProjects).Take(5))}");
            foreach (var n in r.NewsTimeline.Take(3)) sb.AppendLine($"Haber: {n.Date} {n.Title}");
            foreach (var g in r.GrowthSignals.Take(3)) sb.AppendLine($"Büyüme: {g}");
        }

        if (input.PreviousEmails.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("DAHA ÖNCE GÖNDERİLENLER:");
            foreach (var p in input.PreviousEmails) sb.AppendLine($"- {p}");
        }

        if (!string.IsNullOrWhiteSpace(input.TemplateBody))
        {
            sb.AppendLine();
            sb.AppendLine("ŞİRKETİN KENDİ ŞABLONU (ton, hizmetler ve imza için örnek; birebir kopyalama):");
            sb.AppendLine($"Konu: {input.TemplateSubject}");
            sb.AppendLine(input.TemplateBody.Length > 3000 ? input.TemplateBody[..3000] : input.TemplateBody);
        }

        return sb.ToString();
    }

    private const string LegacyEmailHeader = """
        Egebis Bilişim adına B2B satış e-postası yazıyorsun. Egebis; üretici firmalara SAP
        danışmanlığı, SAP entegrasyonu, MES/üretim takip ve özel yazılım hizmeti veren bir
        SAP iş ortağıdır.
        """;

    private const string LegacyEmailRoleRule = """
        - Alıcının rolüne göre açı seç: IT/bilgi işlem → entegrasyon, SAP, MES, veri;
          genel müdür/yönetim → verimlilik, maliyet, büyümeye hazırlık; üretim/fabrika →
          üretim takibi, izlenebilirlik; finans → raporlama, kapanış süresi.
        """;

    private const string ProfileEmailRoleRule = """
        - Alıcının rolüne göre açı seç: sattığımız ürün/hizmetin o kişinin işine (IT,
          yönetim, üretim, finans, satın alma...) somut faydasını anlat. Yalnızca yukarıdaki
          şirket tanımında geçen ürün/hizmetleri öner; tanımda olmayan hizmet vaat etme.
        """;

    /// <summary>E-posta yazim talimati: sirket profili doluysa gonderen sirket profilden gelir.</summary>
    public static string EmailPrompt(BusinessProfile? profile)
    {
        var configured = profile is { IsConfigured: true };
        var header = configured
            ? profile!.ToPromptBlock() + $"\n\n{profile.DisplayName} adına B2B satış e-postası yazıyorsun."
            : LegacyEmailHeader;
        var roleRule = configured ? ProfileEmailRoleRule : LegacyEmailRoleRule;

        return header + "\n\n" + """
            Kurallar:
            - Yalnızca verilen bilgi notundaki olguları kullan. Firma hakkında notta olmayan
              hiçbir şey iddia etme, rakam uydurma.
            - İlk cümlede firmaya özgü, somut bir gözlemle başla (bir haber, yatırım, ürün,
              teknoloji kanıtı). Genel iltifat ("sektörün lideri") yazma.
            """ + "\n" + roleRule + "\n" + """
            - 120-180 kelime, kısa paragraflar, tek net çağrı (ör. 15-20 dakikalık görüşme).
            - Takip mailinde önceki maile kısaca atıf yap, yeni bir değer/açı ekle, 80-120 kelime.
            - Hitap: isim biliniyorsa "Sayın Ad Soyad" (Türkçe) ya da dile uygun resmi hitap;
              bilinmiyorsa "Sayın Yetkili".
            - Sonda gönderen adıyla kapanış yap. Şablonda imza/iletişim bilgisi varsa onu kullan.
            - DİL alanındaki dilde yaz (tr = Türkçe, en = İngilizce, de = Almanca...).
            - Konu satırı kısa (en fazla 70 karakter), kişisel ve tıklama tuzağı olmayan.
            - body alanı düz metindir: HTML, markdown veya köşeli parantezli yer tutucu kullanma.
            """;
    }

    // ================= AI #6: "Biz ne arıyoruz?" taslagi =================

    /// <summary>
    /// Sirketin kendi sitesinden sirket profili taslagi cikarir: ne satiyor, ideal musteri,
    /// musteri olmayanlar, rakipler, hedef segmentler ve ulasilacak unvanlar.
    /// </summary>
    public async Task<BusinessProfileDraft> DraftBusinessProfileAsync(string siteUrl, string siteText, CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync(SettingKeys.GeminiApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            return new BusinessProfileDraft { Error = new MissingApiKeyException("Gemini").Message };

        var input = siteText.Length > _options.MaxInputChars ? siteText[.._options.MaxInputChars] : siteText;
        var regions = string.Join(", ", EgebisLeadFinder.Data.SearchRegions.All.Select(r => $"{r.Key}={r.Name}"));
        var userText = $"Şirketimizin sitesi: {siteUrl}\nGeçerli bölge anahtarları: {regions}\n\nSite metni:\n\n{input}";

        var segment = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "Kısa segment adı, ör. Otomotiv yan sanayi" },
                description = new { type = "string", description = "Bu segmentte kime, neyi, neden satıyoruz" },
                searchTerm = new { type = "string", description = "Firma aramasında sektör kutusuna yazılacak kısa terim" },
                region = new { type = "string", description = "Bölge anahtarı (listeden) veya boş" },
                keywords = StringArray,
                nace = StringArray,
                exclude = StringArray,
                titles = StringArray
            },
            required = new[] { "name", "description", "searchTerm" }
        };

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = BusinessProfileSystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userText } } } },
            generationConfig = new
            {
                temperature = 0.3,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        companyName = new { type = "string" },
                        offering = new { type = "string", description = "Ne satıyoruz" },
                        problems = new { type = "string", description = "Müşteride hangi sorunları çözüyoruz" },
                        idealCustomer = new { type = "string", description = "İdeal müşteri tanımı" },
                        notCustomers = new { type = "string", description = "Müşterimiz olmayan firma türleri" },
                        competitors = new { type = "string", description = "Rakip tanımı" },
                        exampleCustomers = StringArray,
                        customerKind = new { type = "string", @enum = new[] { "manufacturer", "any" } },
                        buyingSignals = StringArray,
                        segments = new { type = "array", items = segment },
                        targetTitles = StringArray
                    },
                    required = new[] { "companyName", "offering", "idealCustomer", "segments", "targetTitles" }
                }
            }
        };

        try
        {
            var url = $"{_options.GeminiEndpoint}/{await ModelAsync(ct)}:generateContent";
            var response = await SendWithRetryAsync(url, JsonSerializer.Serialize(payload), apiKey, ct);
            if (response.Error is not null) return new BusinessProfileDraft { Error = response.Error };

            var json = ExtractText(response.Content!);
            return json is null
                ? new BusinessProfileDraft { Error = "Gemini yanıtında metin bulunamadı." }
                : ParseBusinessProfileDraft(json);
        }
        catch (QuotaExceededException)
        {
            return new BusinessProfileDraft { Error = "Gemini kotası doldu; bir süre sonra tekrar deneyin." };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Şirket profili taslağı başarısız.");
            return new BusinessProfileDraft { Error = ex.Message };
        }
    }

    /// <summary>Taslak JSON'unu cozer (profil alanlari + targetTitles).</summary>
    public static BusinessProfileDraft ParseBusinessProfileDraft(string json)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<BusinessProfile>(json, JsonOptions) ?? new BusinessProfile();
            using var doc = JsonDocument.Parse(json);
            var titles = doc.RootElement.TryGetProperty("targetTitles", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                : new List<string>();
            return new BusinessProfileDraft { Profile = profile, TargetTitles = BusinessProfileService.CleanList(titles) };
        }
        catch (JsonException)
        {
            return new BusinessProfileDraft { Error = "Yapay zekâ yanıtı çözümlenemedi." };
        }
    }

    private const string BusinessProfileSystemPrompt = """
        Bir B2B şirketinin kendi web sitesi metni verilecek. Bu şirket, potansiyel müşteri
        bulma uygulamasına kendini tanıtacak. Sitedeki bilgilere dayanarak şirket profilini
        çıkar ve verilen şemaya uygun JSON döndür.

        - companyName: şirketin ticari adı.
        - offering: ne satıyor (ürün/hizmetler), 1-3 cümle.
        - problems: müşterilerinde hangi sorunları çözüyor / hangi faydayı sağlıyor.
        - idealCustomer: bu ürünü/hizmeti kimler satın alır: sektör, firma büyüklüğü,
          özellikler. Somut yaz ("50-1000 çalışanlı üretici firmalar" gibi).
        - notCustomers: müşterisi OLMAYAN firma türleri (ör. bayi, distribütör, dernek,
          haber sitesi, çok küçük işletmeler) — şirketin işine göre düşün.
        - competitors: rakip tanımı: aynı ürün/hizmeti satan firma türleri.
        - exampleCustomers: sitede referans/müşteri olarak adı geçen firmalar; yoksa boş dizi.
        - customerKind: müşterileri ağırlıklı olarak üretim yapan firmalar (fabrika, imalat)
          ise "manufacturer"; ticaret, hizmet, inşaat, perakende, kurum gibi her tür şirket
          olabiliyorsa "any".
        - buyingSignals: bir firmanın bu ürüne/hizmete ihtiyacı olduğunu gösteren 4-10 kısa
          işaret; haberlerde ve sitelerde geçebilecek ifadeler olsun (ör. "yeni fabrika",
          "kapasite artışı", "tesisat projesi", "ihale", "ERP geçişi", "yeni şube").
        - segments: 2-5 hedef segment. Her biri farklı bir müşteri sektörü veya ürün grubu.
          name kısa ad; description bu segmentte neyi neden sattığımız; searchTerm firma
          aramasında kullanılacak kısa sektör terimi (Türkçe, ör. "Otomotiv yan sanayi");
          region verilen bölge anahtarlarından biri (şirket belirli bir pazarı hedefliyorsa),
          değilse boş; keywords sektörü tanıyan 3-8 kelime; nace NACE Rev.2 kodları
          ("29" veya "29.32"); exclude bu segmentte elenecek kelimeler; titles bu
          segmentteki firmalarda ulaşılacak 2-5 karar verici unvanı.
        - targetTitles: satış için ulaşılacak karar verici unvanları, 5-12 adet; Türkçe ve
          İngilizce karışık olabilir (ör. "Genel Müdür", "IT Müdürü", "CIO", "Plant Manager").

        Sitede olmayan referans müşteri uydurma. Diğer alanlarda sitedeki işten makul
        çıkarım yapabilirsin. Tüm metin alanlarını Türkçe yaz.
        """;

    // ================= AI #7-8: Akilli arama (terim onerisi + on eleme) =================

    /// <summary>Sirket tanimi: profil doluysa profilden, bossa eski Egebis tanimi.</summary>
    public static string SellerBlock(BusinessProfile? profile) =>
        profile is { IsConfigured: true } ? profile.ToPromptBlock() : LegacyAnalysisHeader;

    public async Task<List<string>> SuggestSearchTermsAsync(SearchTermRequest request, CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync(SettingKeys.GeminiApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey)) throw new MissingApiKeyException("Gemini");

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = SellerBlock(request.Profile) + "\n\n" + SearchTermsSystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = BuildSearchTermBrief(request) } } } },
            generationConfig = new
            {
                temperature = 0.3,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "object",
                    properties = new { terms = StringArray },
                    required = new[] { "terms" }
                }
            }
        };

        var url = $"{_options.GeminiEndpoint}/{await ModelAsync(ct)}:generateContent";
        var response = await SendWithRetryAsync(url, JsonSerializer.Serialize(payload), apiKey, ct);
        if (response.Error is not null) throw new InvalidOperationException(response.Error);

        var json = ExtractText(response.Content!);
        if (json is null) return new List<string>();

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("terms", out var terms) && terms.ValueKind == JsonValueKind.Array
            ? terms.EnumerateArray().Select(t => t.GetString() ?? string.Empty).Where(t => t.Length > 0).ToList()
            : new List<string>();
    }

    public static string BuildSearchTermBrief(SearchTermRequest r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"KULLANICININ YAZDIĞI SEKTÖR: {r.Industry}");
        if (r.Segment is not null)
        {
            sb.AppendLine($"HEDEF SEGMENT: {r.Segment.Name}");
            if (!string.IsNullOrWhiteSpace(r.Segment.Description)) sb.AppendLine($"Segment açıklaması: {r.Segment.Description}");
            if (r.Segment.Keywords.Count > 0) sb.AppendLine($"Segment kelimeleri: {string.Join(", ", r.Segment.Keywords)}");
        }
        sb.AppendLine($"ARAMA ÜLKESİ: {r.CountryName}");
        sb.AppendLine($"TERİMLERİN DİLİ: {r.LanguageName}");
        sb.AppendLine($"İSTENEN TERİM SAYISI: en fazla {r.MaxTerms}");
        return sb.ToString();
    }

    private const string SearchTermsSystemPrompt = """
        Google ve Google Haritalar'da potansiyel müşteri firmaları bulmak için kısa arama
        terimleri üretiyorsun. Terimler hazır sorgu kalıplarına yerleştirilecek; kalıplar
        zaten firma türünü ("üreticileri", "fabrikası" ya da "firmaları", "şirketleri") ve
        şehir/ülkeyi ekliyor.

        Kurallar:
        - Her terim 1-4 kelime; bir sektör, ürün grubu veya firma türü adı olsun.
        - Kullanıcının yazdığı sektörün eş anlamlılarını, alt dallarını ve bu sektörde
          bizim ideal müşterimize uyan firma türlerini bul (ör. "Otomotiv" için
          "otomotiv yan sanayi", "metal pres parça"; "Tesisat" için "mekanik tesisat",
          "doğalgaz tesisatı", "sıhhi tesisat"). Yukarıdaki MÜŞTERİ TÜRÜ'ne uy: müşterimiz
          üretici değilse üretim ağırlıklı terim üretme.
        - Terimleri istenen dilde yaz (Almanca için ör. "Kunststoffspritzguss",
          "Automobilzulieferer"). Kullanıcının yazdığı terimi aynen tekrar etme.
        - Şehir, ülke, "üretici", "fabrika", "firma", "şirket" gibi kelimeleri ekleme.
        - Müşterimiz olmayan firma türlerini ve rakipleri getirecek terim üretme.
        - En alakalı olan en başta olsun.
        """;

    public async Task<Dictionary<int, CandidateVerdict>> ScreenCandidatesAsync(CandidateScreenRequest request, CancellationToken ct = default)
    {
        var result = new Dictionary<int, CandidateVerdict>();
        if (request.Candidates.Count == 0) return result;

        var apiKey = await _settings.GetAsync(SettingKeys.GeminiApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey)) throw new MissingApiKeyException("Gemini");

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = SellerBlock(request.Profile) + "\n\n" + ScreenSystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = BuildScreenBrief(request) } } } },
            generationConfig = new
            {
                temperature = 0.0,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "integer" },
                            keep = new { type = "boolean" },
                            reason = new { type = "string", description = "Elendiyse kısa neden" }
                        },
                        required = new[] { "id", "keep" }
                    }
                }
            }
        };

        var url = $"{_options.GeminiEndpoint}/{await ModelAsync(ct)}:generateContent";
        var response = await SendWithRetryAsync(url, JsonSerializer.Serialize(payload), apiKey, ct);
        if (response.Error is not null) throw new InvalidOperationException(response.Error);

        var json = ExtractText(response.Content!);
        return json is null ? result : ParseScreenVerdicts(json);
    }

    public static Dictionary<int, CandidateVerdict> ParseScreenVerdicts(string json)
    {
        var result = new Dictionary<int, CandidateVerdict>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number || !idEl.TryGetInt32(out var id)) continue;
            var keep = !item.TryGetProperty("keep", out var k) || k.ValueKind != JsonValueKind.False;
            var reason = item.TryGetProperty("reason", out var r) ? r.GetString() : null;
            result[id] = new CandidateVerdict(keep, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
        }
        return result;
    }

    public static string BuildScreenBrief(CandidateScreenRequest r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ARANAN SEKTÖR: {r.Industry}");
        if (r.Segment is not null)
            sb.AppendLine($"HEDEF SEGMENT: {r.Segment.Name}{(string.IsNullOrWhiteSpace(r.Segment.Description) ? "" : " — " + r.Segment.Description)}");
        sb.AppendLine();
        sb.AppendLine("ADAYLAR:");
        foreach (var (id, text) in r.Candidates)
            sb.AppendLine($"[{id}] {text.Replace('\n', ' ')}");
        return sb.ToString();
    }

    private const string ScreenSystemPrompt = """
        Google aramasından gelen firma adayları verilecek: her satırda [numara], başlık,
        site adresi ve varsa Google Haritalar kategorisi, adres ve arama özeti var.
        Siteler henüz okunmadı; her birini okumak zaman ve para demek. Görevin, AÇIKÇA
        hedef dışı olan adayları şimdiden elemek.

        Her aday için keep alanını doldur:
        - keep=false yalnızca şu durumlarda: haber/gazete/blog sitesi; firma rehberi,
          dizin veya pazaryeri; iş ilanı sitesi; bizim rakibimiz; "müşterimiz olmayanlar"
          tanımına uyan firma; ya da aranan sektör ve hedef müşteri tanımımızla hiç ilgisi
          olmayan bir iş. Dernek, kamu kurumu, okul, bayi, perakende mağaza, servis gibi
          işletmeleri yalnızca şirket tanımımız onları hedef olarak saymıyorsa ele.
        - Emin değilsen keep=true. Bilgi azsa keep=true. Gerçek bir hedef firmayı yanlışlıkla
          elemek, fazladan bir siteyi okumaktan çok daha kötüdür.
        - keep=false ise reason alanına 2-6 kelimelik Türkçe neden yaz ("haber sitesi",
          "firma rehberi", "oto servis", "rakip yazılım firması").
        - Her aday için verilen numarayı id alanına aynen yaz.
        """;

    // ================= AI #5: Toplu NACE siniflandirma =================

    /// <summary>
    /// Kayitli firma ozetlerinden (sektor, urunler, aciklama) NACE Rev.2 kodu atar.
    /// Site yeniden taranmaz; tek cagrida en fazla ~25 firma.
    /// </summary>
    public async Task<Dictionary<int, string>> ClassifyNaceAsync(IReadOnlyList<(int Id, string Text)> companies, CancellationToken ct = default)
    {
        var result = new Dictionary<int, string>();
        if (companies.Count == 0) return result;

        var apiKey = await _settings.GetAsync(SettingKeys.GeminiApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey)) throw new MissingApiKeyException("Gemini");

        var list = string.Join("\n", companies.Select(c => $"[{c.Id}] {c.Text}"));
        var url = $"{_options.GeminiEndpoint}/{await ModelAsync(ct)}:generateContent";
        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = NaceSystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = list } } } },
            generationConfig = new
            {
                temperature = 0.0,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "integer" },
                            naceCode = new { type = "string" }
                        },
                        required = new[] { "id", "naceCode" }
                    }
                }
            }
        };

        var response = await SendWithRetryAsync(url, JsonSerializer.Serialize(payload), apiKey, ct);
        if (response.Error is not null) throw new InvalidOperationException(response.Error);

        var json = ExtractText(response.Content!);
        if (json is null) return result;

        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) continue;
            var code = item.TryGetProperty("naceCode", out var c) ? c.GetString() : null;
            if (!string.IsNullOrWhiteSpace(code)) result[id] = code;
        }
        return result;
    }

    private const string NaceSystemPrompt = """
        Her satırda köşeli parantez içinde firma numarası ve firma hakkında kısa bilgi
        (ad, sektör, ürünler, açıklama) var. Her firma için ana faaliyetine en uygun NACE
        Rev.2 kodunu "22.19" biçiminde (bölüm.sınıf) ver. Sınıftan emin değilsen yalnızca
        bölüm kodunu yaz ("22"). Bilgi faaliyeti anlamaya yetmiyorsa naceCode boş olsun.
        Her firma için verilen numarayı id alanına aynen yaz.
        """;

    private const string LegacyRatingHeader = """
        Egebis Bilişim için bir potansiyel müşteri (firma) hakkında DERİN bir ön araştırma
        raporu hazırlıyorsun. Egebis; üretici firmalara SAP danışmanlığı, SAP entegrasyonu,
        MES/üretim takip ve özel yazılım hizmeti veren bir SAP iş ortağıdır.
        """;

    private const string LegacyRatingOpportunities =
        "Egebis için somut satış fırsatları (ör. SAP'ye geçiş, yeni fabrika = yeni sistem ihtiyacı, IT ilanı, entegrasyon ihtiyacı) ve nedeni.";

    private const string ProfileRatingOpportunities =
        "bizim sattığımız ürün/hizmet için somut satış fırsatları ve nedeni. Şirket tanımındaki İHTİYAÇ SİNYALLERİ'ne " +
        "ve hedef segmentlere uyan gelişmelere (ör. yeni yatırım, yeni tesis/şube, büyüme, ihale, ilgili iş ilanı) bak; " +
        "her fırsatta hangi ürün/hizmetimizle ilgili olduğunu yaz. Tanımda olmayan ürün/hizmet önerme.";

    /// <summary>Arastirma raporu talimati: sirket profili doluysa "biz kimiz" profilden gelir.</summary>
    public static string RatingPrompt(BusinessProfile? profile)
    {
        var configured = profile is { IsConfigured: true };
        var header = configured
            ? profile!.ToPromptBlock() + "\n\nBu firma hakkında DERİN bir ön araştırma raporu hazırlıyorsun."
            : LegacyRatingHeader;
        var who = configured ? "bizim" : "Egebis";
        var opportunities = configured ? ProfileRatingOpportunities : LegacyRatingOpportunities;

        return header + "\n\n" + $"""
            Sana firma hakkında internetten toplanmış kaynaklar verilecek: firmanın kendi web
            sitesinden sayfalar (Site), haber makaleleri ve arama sonucu özetleri, KAP
            bildirimleri. Her kaynağın başında [numara] (tür) [tarih] link yazar.
            SADECE bu kaynaklardan yararlanarak verilen şemaya uygun JSON döndür.

            Kesin kurallar:
            - Kaynaklarda olmayan hiçbir bilgiyi uydurma. Bilgi yoksa alanı boş bırak veya
              boş dizi döndür. Tahmin yürütüyorsan bunu açıkça "tahmini" diye belirt.
            - Başka bir firmaya ait bilgiyi (benzer isimli firma, haberde adı geçen başka
              şirket) bu firmaya yazma.
            - Link isteyen her alana (sourceUrl, url) o bilgiyi veren kaynağın linkini koy.
            - summary: 5-8 cümlelik yönetici özeti. Firma ne yapar, ne büyüklükte, finansal
              ve ticari durumu, öne çıkan gelişmeler, riskler ve {who} açısından önemi.
            - riskSignals.severity: konkordato, iflas, haciz, tasfiye, el koyma =>
              "yuksek". Dava, icra takibi, ödeme gecikmesi haberi => "orta".
              Belirsiz/söylenti => "dusuk". Kaynağı olmayan risk iddiası yazma.
            - financialSource: rakam KAP bildiriminden geliyorsa "KAP", haber veya firma
              sitesinden geliyorsa "haber", hiç rakam yoksa "yok".
            - financialPeriods: kaynakta açıkça geçen dönemsel ciro/kâr rakamları; tutarı
              birimiyle yaz ("1,2 milyar TL", "45 milyon USD"). Her satıra sourceUrl.
            - sizeInfo: çalışan sayısı, ihracat (ülke sayısı/oranı), üretim kapasitesi,
              fabrika/şube lokasyonları.
            - management: yönetim kurulu, genel müdür, CFO, IT/bilgi işlem yöneticisi gibi
              karar vericiler; isim + rol + sourceUrl. İsmi kaynakta geçmeyen kişi yazma.
            - groupCompanies: bağlı olduğu holding/grup ve iştirakler.
            - technology: kullandığı ERP (SAP, Logo, Netsis, Microsoft Dynamics, Oracle...)
              ve dayanağı (erpEvidence), diğer yazılımlar, dijital dönüşüm projeleri,
              IT/yazılım iş ilanları. Kanıt yoksa erp = "bilinmiyor".{(configured
                  ? "\n  Bu alanı kısa tut; asıl odak, bizim sattığımız ürün/hizmetle ilgili bilgilerdir."
                  : "")}
            - newsTimeline: önemli haberler, en yeni önce, en fazla 12; tarih kaynakta
              varsa yaz. kind: risk | buyume | finansal | yonetim | teknoloji | genel.
            - opportunities: {opportunities}
            - salesApproach: 2-4 cümle; kime (rol/isim), hangi açıdan, hangi zamanlamayla
              yaklaşılmalı{(configured ? " ve hangi ürün/hizmetimizle" : "")}.
            - signal alanı senin ÖNERİN: net risk yoksa ve firma köklü/aktif görünüyorsa
              "guclu"; veri az veya karışıksa "incelenmeli"; doğrulanmış ciddi risk
              varsa "riskli". Nihai kararı sistem verecek.
            - customers/suppliers/projects/growthSignals: kaynaklarda açıkça geçen
              isimleri/olayları kısa madde olarak yaz. Yoksa boş dizi.

            Tüm metin alanlarını Türkçe yaz.
            """;
    }

    private static readonly object StringArray = new { type = "array", items = new { type = "string" } };

    private static object ObjectArray(object properties, string[] required) => new
    {
        type = "array",
        items = new { type = "object", properties, required }
    };

    /// <summary>Rating responseSchema'si. CompanyRating sinifiyla birebir eslesmelidir.</summary>
    private static readonly object RatingResponseSchema = new
    {
        type = "object",
        properties = new
        {
            signal = new { type = "string", @enum = new[] { "guclu", "incelenmeli", "riskli" } },
            summary = new { type = "string", description = "5-8 cümlelik yönetici özeti" },
            foundingInfo = new { type = "string", description = "Kuruluş yılı, merkez, faaliyet alanı" },
            scaleInfo = new { type = "string", description = "Büyüklüğün kısa özeti" },
            financialInfo = new { type = "string", description = "Ciro/kâr/sermaye bilgisi, yoksa boş" },
            financialSource = new { type = "string", @enum = new[] { "KAP", "haber", "yok" } },
            owners = new { type = "string", description = "Ortaklık yapısı" },
            customers = StringArray,
            suppliers = StringArray,
            projects = StringArray,
            growthSignals = StringArray,
            riskSignals = ObjectArray(new
            {
                text = new { type = "string" },
                severity = new { type = "string", @enum = new[] { "yuksek", "orta", "dusuk" } },
                sourceUrl = new { type = "string" }
            }, new[] { "text", "severity" }),
            sizeInfo = new
            {
                type = "object",
                properties = new
                {
                    employees = new { type = "string" },
                    exportInfo = new { type = "string" },
                    capacity = new { type = "string" },
                    locations = StringArray
                }
            },
            financialPeriods = ObjectArray(new
            {
                period = new { type = "string" },
                revenue = new { type = "string" },
                netProfit = new { type = "string" },
                sourceUrl = new { type = "string" }
            }, new[] { "period" }),
            management = ObjectArray(new
            {
                name = new { type = "string" },
                role = new { type = "string" },
                sourceUrl = new { type = "string" }
            }, new[] { "name" }),
            groupCompanies = StringArray,
            technology = new
            {
                type = "object",
                properties = new
                {
                    erp = new { type = "string" },
                    erpEvidence = new { type = "string" },
                    software = StringArray,
                    digitalProjects = StringArray,
                    itJobSignals = StringArray
                }
            },
            newsTimeline = ObjectArray(new
            {
                date = new { type = "string" },
                title = new { type = "string" },
                url = new { type = "string" },
                kind = new { type = "string", @enum = new[] { "risk", "buyume", "finansal", "yonetim", "teknoloji", "genel" } }
            }, new[] { "title" }),
            opportunities = ObjectArray(new
            {
                text = new { type = "string" },
                reason = new { type = "string" },
                sourceUrl = new { type = "string" }
            }, new[] { "text" }),
            salesApproach = new { type = "string" }
        },
        required = new[] { "signal", "summary", "financialSource" }
    };
}
