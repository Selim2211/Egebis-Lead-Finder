using System.Text.Json;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Localization;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>AI #9+: rapor yapay zekasi (firma karsilastirma, sektor analizi, ICP onerisi).</summary>
public partial class GeminiAiService : IInsightAi
{
    /// <summary>Semali JSON istegi atar; yanit metnini (JSON) veya hata mesajini doner.</summary>
    private async Task<(string? Json, string? Error)> AskJsonAsync(string systemPrompt, string userText, object schema,
        double temperature, CancellationToken ct, bool localize = true)
    {
        var apiKey = await _settings.GetAsync(SettingKeys.GeminiApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey)) return (null, new MissingApiKeyException("Gemini").Message);

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = localize ? Loc.Prompt(systemPrompt) : systemPrompt } } },
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
    // ================= NACE esleme + sektor analizi (Faz-II madde 4) =================

    public async Task<NaceMatchAiResult> MatchNaceAsync(string keyword, CancellationToken ct = default)
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                codes = ObjectArray(new
                {
                    code = new { type = "string", description = "NACE Rev.2 kodu: bölüm \"22\" veya sınıf \"22.22\"" },
                    name = new { type = "string", description = "Kodun resmi Türkçe adı" },
                    reason = new { type = "string", description = "Anahtar kelimeyle ilişkisi, kısa" }
                }, new[] { "code", "name" })
            },
            required = new[] { "codes" }
        };

        var (json, error) = await AskJsonAsync(NaceMatchPrompt, $"Anahtar kelime: {keyword}", schema, 0.1, ct);
        if (error is not null) return new NaceMatchAiResult { Error = error };
        return ParseNaceMatches(json!);
    }

    public static NaceMatchAiResult ParseNaceMatches(string json)
    {
        var result = new NaceMatchAiResult();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("codes", out var codes) || codes.ValueKind != JsonValueKind.Array) return result;
            foreach (var item in codes.EnumerateArray())
            {
                var code = NaceCatalog.Normalize(item.TryGetProperty("code", out var c) ? c.GetString() : null);
                if (code is null || NaceCatalog.Division(code) is null) continue; // uydurma kodlari at
                if (result.Codes.Any(x => x.Code == code)) continue;
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                var reason = item.TryGetProperty("reason", out var r) ? r.GetString() : null;
                result.Codes.Add(new NaceSuggestion(code, string.IsNullOrWhiteSpace(name) ? NaceCatalog.Division(code)!.Name : name!.Trim(), reason));
            }
        }
        catch (JsonException)
        {
            result.Error = "Yapay zekâ yanıtı çözümlenemedi.";
        }
        return result;
    }

    private const string NaceMatchPrompt = """
        Kullanıcı bir sektörü günlük dille tarif eden anahtar kelime yazacak (ör. "plastik
        ambalaj", "otomotiv yan sanayi", "süt ürünleri"). NACE Rev.2 sınıflandırmasında bu
        sektöre karşılık gelen kodları bul.

        - En alakalı 3-8 kodu, en alakalı en başta olacak şekilde döndür.
        - Hem bölüm (2 hane, "22") hem sınıf (4 hane, "22.22") kullanabilirsin; mümkünse
          sınıf düzeyinde ol, genel bir kelimede bölümü de ekle.
        - name: kodun resmi Türkçe adı (TÜİK NACE Rev.2).
        - reason: bu kodun anahtar kelimeyle ilişkisi, en fazla 12 kelime.
        - Var olmayan kod uydurma.
        """;

    public async Task<SectorAiResult> AnalyzeSectorsAsync(BusinessProfile? profile, string keyword,
        IReadOnlyList<SectorInput> sectors, CancellationToken ct = default)
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                overview = new { type = "string", description = "Tüm sektörler için 2-4 cümlelik genel değerlendirme" },
                sectors = ObjectArray(new
                {
                    code = new { type = "string" },
                    name = new { type = "string" },
                    fitScore = new { type = "integer", description = "0-100: bu sektör bize ne kadar uygun" },
                    verdict = new { type = "string", @enum = new[] { "cok_uygun", "uygun", "kismen", "uygun_degil" } },
                    summary = new { type = "string" },
                    reasons = StringArray,
                    risks = StringArray,
                    needs = StringArray,
                    idealCompany = new { type = "string" },
                    searchTerms = StringArray,
                    titles = StringArray
                }, new[] { "code", "fitScore", "verdict", "summary", "reasons", "risks", "needs", "searchTerms", "titles" })
            },
            required = new[] { "overview", "sectors" }
        };

        var brief = new System.Text.StringBuilder();
        brief.AppendLine($"Kullanıcının aradığı sektör: {keyword}");
        brief.AppendLine("Değerlendirilecek NACE kodları ve uygulamamızdaki kayıtlar:");
        foreach (var s in sectors)
        {
            brief.Append($"- {s.Code} {s.Name}: uygulamada {s.Stats.Companies} firma");
            if (s.Stats.Companies > 0)
                brief.Append($", ortalama lead puanı {s.Stats.AvgScore}, ortalama profil uygunluğu {(s.Stats.AvgFit is int f ? $"%{f}" : "bilinmiyor")}, " +
                             $"ICP'ye uyan {s.Stats.IcpMatches}, lead {s.Stats.Leads}, proje başlayan {s.Stats.Projects}");
            brief.AppendLine();
        }

        var (json, error) = await AskJsonAsync(SellerBlock(profile) + "\n\n" + SectorPrompt, brief.ToString(), schema, 0.3, ct);
        if (error is not null) return new SectorAiResult { Error = error };
        return ParseSectorResult(json!, sectors);
    }

    public static SectorAiResult ParseSectorResult(string json, IReadOnlyList<SectorInput> sectors)
    {
        try
        {
            var result = JsonSerializer.Deserialize<SectorAiResult>(json, JsonOptions) ?? new SectorAiResult();
            result.Error = null;

            // Yalnizca istenen kodlar, istenen sirayla; eksik kalan sektor "degerlendirilemedi" olur.
            var ordered = new List<SectorAssessment>();
            foreach (var input in sectors)
            {
                var match = result.Sectors.FirstOrDefault(x => NaceCatalog.Normalize(x.Code) == input.Code)
                    ?? new SectorAssessment { Summary = "Yapay zekâ bu sektör için değerlendirme döndürmedi.", Verdict = "kismen" };
                match.Code = input.Code;
                match.Name = input.Name;
                match.FitScore = Math.Clamp(match.FitScore, 0, 100);
                match.Verdict = match.Verdict is "cok_uygun" or "uygun" or "kismen" or "uygun_degil" ? match.Verdict : "kismen";
                match.Stats = input.Stats;
                ordered.Add(match);
            }
            result.Sectors = ordered;
            return result;
        }
        catch (JsonException)
        {
            return new SectorAiResult { Error = "Yapay zekâ yanıtı çözümlenemedi." };
        }
    }

    private const string SectorPrompt = """
        Satış ekibimiz yeni hedef sektörler değerlendiriyor. Sana NACE Rev.2 kodlarıyla
        sektörler ve uygulamamızda o sektörde kayıtlı firmaların özeti verilecek.
        Yukarıdaki şirket tanımımıza göre her sektörün bizim için ne kadar uygun bir hedef
        pazar olduğunu değerlendir ve verilen şemaya uygun JSON döndür.

        Her sektör için:
        - fitScore: 0-100 uygunluk. 80+: ürünümüze doğrudan ihtiyacı olan, satın alma gücü
          olan ana hedef; 50-79: uygun ama seçici olunmalı; 25-49: kısmen; 0-24: uygun değil.
        - verdict: cok_uygun (80+), uygun (60-79), kismen (30-59), uygun_degil (<30).
        - summary: 2-3 cümle, bu sektör bize neden uygun / değil.
        - reasons: 2-4 uygunluk gerekçesi; risks: 1-3 risk veya zorluk.
        - needs: bu sektördeki firmaların çözdüğümüz 2-4 tipik ihtiyacı / sorunu.
        - idealCompany: bu sektörde hedeflenecek firma profili (ölçek, üretim tipi vb.), 1 cümle.
        - searchTerms: Firma Ara'da kullanılacak 2-4 kısa Türkçe arama terimi.
        - titles: bu sektörde ulaşılacak 2-4 karar verici unvanı.
        - Uygulamadaki istatistikler (firma sayısı, ortalama puan, lead, proje) varsa
          değerlendirmede kanıt olarak kullan; yoksa genel sektör bilgisine dayan.

        overview: tüm sektörleri kıyaslayan 2-4 cümle; hangisine öncelik verilmeli söyle.
        Tüm metinleri Türkçe yaz. Uydurma rakam verme.
        """;
    // ================= Siteden ICP onerisi (Faz-II madde 5) =================

    public async Task<IcpSuggestion> SuggestIcpAsync(string siteUrl, string siteText, CancellationToken ct = default)
    {
        var input = siteText.Length > _options.MaxInputChars ? siteText[.._options.MaxInputChars] : siteText;
        var schema = new
        {
            type = "object",
            properties = new
            {
                companySummary = new { type = "string", description = "Şirket ne yapıyor, 1-2 cümle" },
                idealCustomer = new { type = "string", description = "İdeal müşteri tanımı, 2-3 cümle" },
                nace = ObjectArray(new
                {
                    code = new { type = "string", description = "Müşteri sektörünün NACE Rev.2 kodu (\"22\" veya \"22.22\")" },
                    name = new { type = "string" },
                    reason = new { type = "string" }
                }, new[] { "code", "name", "reason" }),
                industryKeywords = StringArray,
                countries = StringArray,
                cities = StringArray,
                locationReason = new { type = "string" },
                minEmployees = new { type = "integer", description = "Asgari çalışan sayısı; şart yoksa 0" },
                minEmployeesReason = new { type = "string" },
                requireManufacturer = new { type = "boolean" },
                manufacturerReason = new { type = "string" },
                excludeKeywords = StringArray,
                excludeReason = new { type = "string" },
                targetTitles = StringArray
            },
            required = new[]
            {
                "companySummary", "idealCustomer", "nace", "industryKeywords", "countries", "cities", "minEmployees",
                "minEmployeesReason", "requireManufacturer", "manufacturerReason", "excludeKeywords", "targetTitles"
            }
        };

        var (json, error) = await AskJsonAsync(IcpPrompt, $"Şirketimizin sitesi: {siteUrl}\n\nSite metni:\n\n{input}", schema, 0.2, ct);
        if (error is not null) return new IcpSuggestion { Error = error, Website = siteUrl };

        var result = ParseIcpSuggestion(json!);
        result.Website = siteUrl;
        return result;
    }

    public static IcpSuggestion ParseIcpSuggestion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
            List<string> List(string name, int max) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                ? BusinessProfileService.CleanList(v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null)).Take(max).ToList()
                : new List<string>();

            var result = new IcpSuggestion
            {
                CompanySummary = Str("companySummary"),
                IdealCustomer = Str("idealCustomer"),
                IndustryKeywords = List("industryKeywords", 12),
                Countries = List("countries", 10),
                // Il onerileri yalnizca gercek il adlariyla sinirli (yazim farki duzeltilir).
                Cities = List("cities", 20)
                    .Select(c => EgebisLeadFinder.Data.TurkishProvinces.All.FirstOrDefault(p => TurkishText.Normalize(p) == TurkishText.Normalize(c)))
                    .OfType<string>().Distinct().ToList(),
                LocationReason = Str("locationReason"),
                MinEmployees = root.TryGetProperty("minEmployees", out var me) && me.TryGetInt32(out var n) ? Math.Clamp(n, 0, 100000) : 0,
                MinEmployeesReason = Str("minEmployeesReason"),
                RequireManufacturer = root.TryGetProperty("requireManufacturer", out var rm) && rm.ValueKind == JsonValueKind.True,
                ManufacturerReason = Str("manufacturerReason"),
                ExcludeKeywords = List("excludeKeywords", 15),
                ExcludeReason = Str("excludeReason"),
                TargetTitles = List("targetTitles", 12)
            };

            if (root.TryGetProperty("nace", out var nace) && nace.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in nace.EnumerateArray())
                {
                    var code = NaceCatalog.Normalize(item.TryGetProperty("code", out var c) ? c.GetString() : null);
                    if (code is null || NaceCatalog.Division(code) is null || result.Nace.Any(x => x.Code == code)) continue;
                    var name = item.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                    var reason = item.TryGetProperty("reason", out var r) ? r.GetString() : null;
                    result.Nace.Add(new NaceSuggestion(code, string.IsNullOrWhiteSpace(name) ? NaceCatalog.Division(code)!.Name : name!.Trim(), reason));
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return new IcpSuggestion { Error = "Yapay zekâ yanıtı çözümlenemedi." };
        }
    }

    private const string IcpPrompt = """
        Bir B2B şirketinin kendi web sitesi metni verilecek. Bu şirket, potansiyel müşteri
        bulma uygulamasında "İdeal Müşteri Profili (ICP)" tanımlayacak. Sitedeki ürün ve
        hizmetlere bakarak bu şirketin MÜŞTERİLERİNİN (şirketin kendisinin değil) profilini
        çıkar ve verilen şemaya uygun JSON döndür.

        - companySummary: şirket ne satıyor, 1-2 cümle.
        - idealCustomer: bu ürünleri kim satın alır; sektör, ölçek, özellikler; 2-3 cümle.
        - nace: müşterilerin faaliyet gösterdiği 3-8 NACE Rev.2 kodu, en önemlisi başta;
          her biri için kısa gerekçe. Şirketin kendi NACE kodunu değil, müşterilerinkini yaz.
        - industryKeywords: müşteri sektörlerini tanıyan 4-10 Türkçe kelime ("plastik", "kalıp").
        - countries / cities: site belirli bir pazarı işaret ediyorsa hedef ülke(ler) ve
          Türkiye illeri; işaret yoksa ülke olarak şirketin bulunduğu ülkeyi yaz, il listesi boş.
          locationReason: kısa gerekçe.
        - minEmployees: ürün küçük işletmelere uygun değilse makul asgari çalışan sayısı
          (ör. 20, 50, 100); ölçek şartı yoksa 0. minEmployeesReason: kısa gerekçe.
        - requireManufacturer: müşteriler üretim yapan firmalar olmalıysa true.
          manufacturerReason: kısa gerekçe.
        - excludeKeywords: müşteri olmayan firma türlerini eleyecek 3-10 kelime (ör. "bayi",
          "distribütör", "danışmanlık", rakip türleri). excludeReason: kısa gerekçe.
        - targetTitles: müşteride ulaşılacak 4-10 karar verici unvanı.

        Sitede olmayan bilgiyi uydurma; makul çıkarım yapabilirsin. Tüm metinleri Türkçe yaz.
        """;
}
