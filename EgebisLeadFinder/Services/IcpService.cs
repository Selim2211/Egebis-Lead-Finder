using System.Text.Json;
using EgebisLeadFinder.Controllers;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services;

/// <summary>AI #5: kayitli firma ozetlerinden NACE kodu. GeminiAiService uygular.</summary>
public interface INaceClassifierAi
{
    Task<Dictionary<int, string>> ClassifyNaceAsync(IReadOnlyList<(int Id, string Text)> companies, CancellationToken ct = default);

    /// <summary>
    /// Urun eslestirme: her firma icin verilen urun listesinden HANGILERINI urettigini (kendi uretimi ya da kendi markasi)
    /// dondurur; hicbirini uretmiyorsa bos liste ve kisa neden. Liste disi urun yazilmaz (cagiran ayrica dogrular).
    /// </summary>
    Task<Dictionary<int, ProductVerdict>> MatchProductsAsync(IReadOnlyList<(int Id, string Text)> companies, IReadOnlyList<string> products, CancellationToken ct = default);
}

/// <summary>
/// Bir firmanin urettigi (listedeki) urunler; bossa neden ("bayi", "servis"...).
/// Kind: "uretici" | "fason" | "yan_sanayi" | "bayi" | "emin_degil" (bos = eski bicim). Evidence: firma ozetindeki «alintilardan» biri.
/// </summary>
public record ProductVerdict(List<string> Products, string? Reason, string? Kind = null, string? Evidence = null);

/// <summary>Listede kalan firmanin turu (sonuc ekraninda etiket olarak gosterilir).</summary>
public enum FirmKind
{
    /// <summary>Kendi fabrikasinda / kendi markasiyla uretiyor, sitesinden alinti ile kanitli.</summary>
    Producer,

    /// <summary>Baskalari icin fason (sozlesmeli) uretim yapiyor, sitesinden alinti ile kanitli.</summary>
    Contract,

    /// <summary>Uretici gibi duruyor ama sitesinden gosterilebilir bir kanit yok: "emin degil".</summary>
    Uncertain
}

/// <summary>
/// Ideal musteri profilini (ICP) okur/yazar ve firma puanini ICP'ye gore gunceller.
/// Tum puanlama noktalari (kesif, yeniden analiz, e-posta acma, lead bulma) bunu kullanir.
/// </summary>
public class IcpService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ISettingsService _settings;
    private readonly LeadScoringService _scoring;
    private readonly ApplicationDbContext _db;
    private readonly INaceClassifierAi _naceAi;
    private IcpProfile? _cached;
    private (BusinessProfile Profile, FitContext Fit)? _fitCached;

    public IcpService(ISettingsService settings, LeadScoringService scoring, ApplicationDbContext db, INaceClassifierAi naceAi)
    {
        _settings = settings;
        _scoring = scoring;
        _db = db;
        _naceAi = naceAi;
    }

    public const int NaceBatchSize = 25;

    /// <summary>
    /// NACE kodu bos firmalara kayitli analizden kod atar ve ICP'ye gore yeniden puanlar.
    /// Site taranmaz; puan, sektor adi ve analiz degismez (yalnizca NACE ve ICP uyumu).
    /// </summary>
    public async Task<(int Filled, int Total)> FillMissingNaceAsync(IProgress<Progress.JobStep>? progress = null, CancellationToken ct = default)
    {
        var candidates = await _db.Companies.AsNoTracking()
            .Where(c => c.NaceCode == null && (c.AiAnalysis != null || c.Industry != null || c.Description != null))
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Name, c.Industry, c.Description, c.AiAnalysis })
            .ToListAsync(ct);

        var filled = 0;
        var batches = candidates.Chunk(NaceBatchSize).ToList();
        for (var i = 0; i < batches.Count; i++)
        {
            progress?.Report(new Progress.JobStep(5 + 90 * i / Math.Max(1, batches.Count), "NACE kodları belirleniyor",
                $"{i * NaceBatchSize}/{candidates.Count} firma"));

            var items = batches[i].Select(c => (c.Id, NaceBrief(c.Name, c.Industry, c.Description, c.AiAnalysis))).ToList();
            var codes = await _naceAi.ClassifyNaceAsync(items, ct);

            var ids = codes.Keys.ToList();
            var companies = await _db.Companies.Include(c => c.Contacts).Where(c => ids.Contains(c.Id)).ToListAsync(ct);
            foreach (var company in companies)
            {
                var code = NaceCatalog.ResolveCode(codes[company.Id]);
                if (code is null) continue;   // resmi listede olmayan (uydurma) kod atanmaz
                company.NaceCode = code;
                await ScoreAsync(company, CompanyController.ParseAnalysis(company.AiAnalysis), site: null, ct);
                filled++;
            }

            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }

        return (filled, candidates.Count);
    }

    /// <summary>
    /// NACE (urun) aramasinda: her firmaya "bu urunlerden hangilerini uretiyorsun?" diye sorulur (site analizindeki urunler,
    /// sektor, uretici bilgisi uzerinden; site yeniden okunmaz). Yalniz verilen listedeki urunler kabul edilir. Yapay zeka
    /// kesintisinde Ok=false doner: cagiran o zaman kimseyi elemez.
    /// </summary>
    public async Task<ProductMatchResult> MatchProductsAsync(IReadOnlyList<Company> companies, IReadOnlyList<string> products, CancellationToken ct = default,
        IReadOnlyDictionary<Company, ProducerEvidence>? proof = null)
    {
        var result = new ProductMatchResult();
        if (companies.Count == 0 || products.Count == 0) return result;
        // Buyuk/kucuk harf ya da aksan farkiyla ayni urun iki kez gelirse tek kayit kalir (ToDictionary cokerdi).
        var allowed = products.GroupBy(p => TurkishText.Normalize(p)).ToDictionary(g => g.Key, g => g.First());

        foreach (var batch in companies.Chunk(NaceBatchSize))
        {
            var items = batch.Select((c, i) => (i + 1, NaceBrief(c.Name, c.Industry, c.Description, c.AiAnalysis) + EvidenceBrief(proof, c))).ToList();
            var briefs = items.ToDictionary(x => x.Item1, x => x.Item2);
            Dictionary<int, ProductVerdict> verdicts;
            try
            {
                verdicts = await _naceAi.MatchProductsAsync(items, products, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new ProductMatchResult { Ok = false };
            }

            for (var i = 0; i < batch.Length; i++)
            {
                verdicts.TryGetValue(i + 1, out var v);
                var company = batch[i];
                var made = (v?.Products ?? new List<string>())
                    .Select(p => allowed.GetValueOrDefault(TurkishText.Normalize(p)))
                    .OfType<string>()
                    .Distinct()
                    .ToList();
                var reason = string.IsNullOrWhiteSpace(v?.Reason) ? null : v!.Reason!.Trim();

                switch (NormalizeKind(v?.Kind))
                {
                    case "bayi":
                        result.Reasons[company] = reason ?? "bayi / satıcı";
                        break;

                    case "yan_sanayi":
                        result.Reasons[company] = reason ?? "yan sanayi (parça)";
                        break;

                    case "emin_degil":
                        // Karar verilemedi: listeden atilmaz, "emin degil" etiketiyle sona konur.
                        if (made.Count > 0) result.Matches[company] = made;
                        result.Kinds[company] = FirmKind.Uncertain;
                        break;

                    case "uretici" or "fason":
                        if (made.Count == 0)
                        {
                            result.Reasons[company] = reason ?? "bu ürünleri üretmiyor";
                            break;
                        }
                        result.Matches[company] = made;
                        // "Uretiyor" demek icin sitenin kendi cumlesi gosterilmeli: yapay zekanin dondurdugu alinti ozette
                        // gercekten varsa o; yoksa kural katmaninin buldugu uretim kaniti. Hicbiri yoksa "emin degil".
                        var quote = VerifyQuote(v?.Evidence, briefs[i + 1]) ?? RuleQuote(proof, company);
                        if (quote is null)
                        {
                            result.Kinds[company] = FirmKind.Uncertain;
                        }
                        else
                        {
                            result.Kinds[company] = NormalizeKind(v?.Kind) == "fason" ? FirmKind.Contract : FirmKind.Producer;
                            result.Quotes[company] = quote;
                        }
                        break;

                    default:   // eski bicim (tur yok): urun listesi doluysa uretici, degilse neden
                        if (made.Count > 0) result.Matches[company] = made;
                        else result.Reasons[company] = reason ?? "bu ürünleri üretmiyor";
                        break;
                }
            }
        }
        return result;
    }

    private static string? NormalizeKind(string? kind) => string.IsNullOrWhiteSpace(kind) ? null : TurkishText.Normalize(kind).Trim().Replace(' ', '_').Replace('-', '_');

    private static string? RuleQuote(IReadOnlyDictionary<Company, ProducerEvidence>? proof, Company company) =>
        proof is not null && proof.TryGetValue(company, out var e) && e.Kind == ProducerKind.Producer ? e.ProducerQuotes.FirstOrDefault() : null;

    /// <summary>
    /// Yapay zekanin "alinti" diye dondurdugu metin firma ozetinde AYNEN geciyor mu? Gecmiyorsa uydurmadir: null.
    /// Karsilastirma Turkce harf, bosluk ve tirnak farklarini yok sayar; cok kisa metin kanit sayilmaz.
    /// </summary>
    private const string NoEvidenceMarker = "Sitede somut üretim kanıtı bulunamadı";

    public static string? VerifyQuote(string? evidence, string brief)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return null;

        var cleaned = System.Text.RegularExpressions.Regex.Replace(evidence.Trim().Trim('«', '»', '"', '“', '”', '…', ' '), @"\s+", " ");
        if (cleaned.Length < 12) return null;

        // "Sitede somut uretim kaniti bulunamadi" bizim yazdigimiz bir not; yapay zeka bunu "alinti" diye geri verse de kanit sayilmaz.
        var haystack = System.Text.RegularExpressions.Regex.Replace(TurkishText.Normalize(brief.Replace(NoEvidenceMarker, " ")), @"\s+", " ");
        return haystack.Contains(TurkishText.Normalize(cleaned), StringComparison.Ordinal) ? cleaned : null;
    }

    /// <summary>
    /// Sitenin kendi metninden alinan uretim / bayi kanitlari, yapay zekaya gonderilen firma ozetine eklenir. Alintilar aynen
    /// sitedendir (kural katmani cikarir); yapay zeka bunlara dayanir, kendi bilgisinden "uretir" demez.
    /// </summary>
    public static string EvidenceBrief(IReadOnlyDictionary<Company, ProducerEvidence>? proof, Company company)
    {
        if (proof is null || !proof.TryGetValue(company, out var e)) return string.Empty;
        var parts = new List<string>();
        if (e.ProducerQuotes.Count > 0) parts.Add("Sitede üretim kanıtı: " + string.Join(" / ", e.ProducerQuotes.Select(q => "«" + q + "»")));
        if (e.TraderQuotes.Count > 0) parts.Add("Sitede bayi/ticaret işareti: " + string.Join(" / ", e.TraderQuotes.Select(q => "«" + q + "»")));
        if (parts.Count == 0) parts.Add(NoEvidenceMarker);
        return " | " + string.Join(" | ", parts).Replace('\n', ' ');
    }

    /// <summary>Urun eslestirme sonucu: ureten firmalar (hangi urunler) ve uretmeyenlerin nedeni.</summary>
    public class ProductMatchResult
    {
        public bool Ok { get; set; } = true;
        public Dictionary<Company, List<string>> Matches { get; } = new();
        public Dictionary<Company, string> Reasons { get; } = new();

        /// <summary>Listede kalan firmanin turu: uretici / fason / emin degil. Yalniz tur bildiren yapay zeka yanitlarinda dolar.</summary>
        public Dictionary<Company, FirmKind> Kinds { get; } = new();

        /// <summary>Uretim iddiasini destekleyen, firma sitesinden aynen alinmis cumle.</summary>
        public Dictionary<Company, string> Quotes { get; } = new();
    }

    public static string NaceBrief(string name, string? industry, string? description, string? aiAnalysis)
    {
        var analysis = CompanyController.ParseAnalysis(aiAnalysis);
        var parts = new List<string> { name };
        if (!string.IsNullOrWhiteSpace(analysis?.Industry ?? industry)) parts.Add("Sektör: " + (analysis?.Industry ?? industry));
        if (analysis is { Products.Count: > 0 }) parts.Add("Ürünler: " + string.Join(", ", analysis.Products.Take(6)));
        if (analysis is not null) parts.Add(analysis.Manufacturer ? "üretici" : "üretici değil");
        var desc = analysis?.Reason ?? description;
        if (!string.IsNullOrWhiteSpace(desc)) parts.Add(desc.Length > 300 ? desc[..300] : desc);
        return string.Join(" | ", parts).Replace('\n', ' ');
    }

    public async Task<IcpProfile> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        var raw = await _settings.GetAsync(SettingKeys.IcpProfile, ct);
        try
        {
            _cached = string.IsNullOrWhiteSpace(raw) ? new IcpProfile() : JsonSerializer.Deserialize<IcpProfile>(raw, JsonOptions) ?? new IcpProfile();
        }
        catch (JsonException)
        {
            _cached = new IcpProfile();
        }
        return _cached;
    }

    public async Task SaveAsync(IcpProfile profile, CancellationToken ct = default)
    {
        static List<string> Clean(IEnumerable<string> items) =>
            items.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        profile.NaceCodes = Clean(profile.NaceCodes);
        profile.IndustryKeywords = Clean(profile.IndustryKeywords);
        profile.Cities = Clean(profile.Cities);
        profile.Countries = Clean(profile.Countries);
        profile.ExcludeKeywords = Clean(profile.ExcludeKeywords);
        profile.MinEmployees = Math.Max(0, profile.MinEmployees);
        profile.LocationWeight = Math.Clamp(profile.LocationWeight, 0, 30);

        await _settings.SetManyAsync(new Dictionary<string, string?>
        {
            [SettingKeys.IcpProfile] = JsonSerializer.Serialize(profile)
        }, ct);
        _cached = profile;
    }

    /// <summary>"Şirketimiz" profili ve puanlama baglami (profil + hedef unvanlar), ornek basina bir kez okunur.</summary>
    public async Task<(BusinessProfile Profile, FitContext Fit)> GetFitAsync(CancellationToken ct = default)
    {
        if (_fitCached is { } cached) return cached;
        var profile = BusinessProfileService.Parse(await _settings.GetAsync(SettingKeys.BusinessProfile, ct));
        var fit = profile.IsConfigured
            ? new FitContext(true, (await _settings.GetTitleKeywordsAsync(ct))
                .Concat(profile.Segments.SelectMany(s => s.TargetTitles))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(), profile.ScoringSignals)
            : FitContext.Inactive;
        _fitCached = (profile, fit);
        return _fitCached.Value;
    }

    /// <summary>Kaydetmeden puan dokumu (firma detayi icin).</summary>
    public async Task<ScoreBreakdown> BreakdownAsync(Company company, CompanyAnalysis? analysis, CancellationToken ct = default)
    {
        var (_, fit) = await GetFitAsync(ct);
        return _scoring.ScoreCompany(analysis, site: null, company.Contacts, await GetAsync(ct), company, fit);
    }

    /// <summary>Firmanin puanini, ICP uyumunu ve NACE kodunu gunceller (kaydetmez).</summary>
    public async Task<ScoreBreakdown> ScoreAsync(Company company, CompanyAnalysis? analysis, ScrapedSite? site, CancellationToken ct = default)
    {
        var icp = await GetAsync(ct);
        var (profile, fit) = await GetFitAsync(ct);
        var breakdown = _scoring.ScoreCompany(analysis, site, company.Contacts, icp, company, fit);
        ApplyFit(company, analysis, profile);

        company.Score = breakdown.Total;
        company.IcpMatch = breakdown.IcpMatch;

        if (breakdown.DisqualifiedReason is not null)
        {
            company.EvaluationStatus = EvaluationStatus.Disqualified;
            company.EvaluationNote = breakdown.DisqualifiedReason.Length > 500 ? breakdown.DisqualifiedReason[..500] : breakdown.DisqualifiedReason;
        }
        else if (analysis is null)
        {
            // Yapay zeka analizi yok: puan yalnizca kaba sinyallerden, firma incelenmedi sayilir.
            company.MarkNotEvaluated(company.ProcessingError is { Length: > 0 } err
                ? $"Yapay zekâ analizi yapılamadı: {err}"
                : "Yapay zekâ analizi yapılamadı.");
        }
        else
        {
            company.EvaluationStatus = EvaluationStatus.Evaluated;
            company.EvaluationNote = null;
        }
        if (!string.IsNullOrWhiteSpace(analysis?.NaceCode))
        {
            // Yapay zekanin serbest urettigi kod resmi listede yoksa (ör. 20.22, 28.32) kullanilmaz: kod bos kalir ve
            // sonradan resmi listeden secilir.
            var proposed = NaceCatalog.ResolveCode(analysis.NaceCode);
            if (proposed is not null) company.NaceCode = proposed;
        }

        return breakdown;
    }

    /// <summary>Analizden uygunluk puani, segment ve gerekceyi firmaya yazar.</summary>
    public static void ApplyFit(Company company, CompanyAnalysis? analysis, BusinessProfile profile)
    {
        if (analysis is null) return;

        var reason = analysis.Reason?.Trim();
        company.FitReason = string.IsNullOrEmpty(reason) ? null : reason.Length > 500 ? reason[..500] : reason;

        if (profile.IsConfigured && analysis.FitScore is int score)
        {
            company.FitScore = analysis.Potential && !analysis.SapVendor ? Math.Clamp(score, 0, 100) : Math.Min(Math.Clamp(score, 0, 100), 19);
            company.FitSegment = analysis.Potential ? profile.FindSegment(analysis.Segment)?.Name : null;
        }
        else
        {
            company.FitScore = null;
            company.FitSegment = null;
        }
    }

    /// <summary>Tum firmalari kayitli AI analizi ve kisilerle yeniden puanlar (API/kredi harcamaz).</summary>
    public async Task<int> RescoreAllAsync(CancellationToken ct = default)
    {
        var ids = await _db.Companies.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Id).ToListAsync(ct);
        var changed = 0;

        foreach (var chunk in ids.Chunk(200))
        {
            var companies = await _db.Companies.Include(c => c.Contacts)
                .Where(c => chunk.Contains(c.Id)).ToListAsync(ct);

            foreach (var company in companies)
            {
                var before = (company.Score, company.IcpMatch, company.NaceCode, company.FitSegment);
                await ScoreAsync(company, CompanyController.ParseAnalysis(company.AiAnalysis), site: null, ct);
                if (before != (company.Score, company.IcpMatch, company.NaceCode, company.FitSegment)) changed++;
            }

            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }

        return changed;
    }
}
