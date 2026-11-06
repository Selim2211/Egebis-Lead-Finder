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
    /// Kapali liste secimi: her firma icin verilen RESMI 6 haneli kodlardan (kod -> ad) en uygun tekini secer; hicbiri
    /// uymuyorsa firma sonucta yoktur. Serbest kod uretmez, dolayisiyla uydurma kod gelmez.
    /// </summary>
    Task<Dictionary<int, string>> ChooseNaceAsync(IReadOnlyList<(int Id, string Text)> companies, IReadOnlyList<KeyValuePair<string, string>> options, CancellationToken ct = default);
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
                var code = NaceCatalog.Normalize(codes[company.Id]);
                if (code is null || !NaceCatalog.Exists(code)) continue;   // resmi listede olmayan (uydurma) kod atanmaz
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
    /// NACE kodlu aramada: kodu bos kalan firmalara (dernek, rehber, analizi eksik olanlar) kayitli analizden toplu NACE atar,
    /// boylece kodu bilinmeyen firma sizmasin. Yalniz bellekteki varliklara yazar (kaydetmek cagiranin isi); site taranmaz.
    /// </summary>
    public async Task<NaceAssignResult> AssignMissingNaceAsync(IReadOnlyList<Company> companies, CancellationToken ct = default)
    {
        var missing = companies
            .Where(c => string.IsNullOrWhiteSpace(c.NaceCode) && (c.AiAnalysis != null || c.Industry != null || c.Description != null))
            .ToList();
        var filled = 0;
        var unresolved = new List<Company>();
        foreach (var batch in missing.Chunk(NaceBatchSize))
        {
            var items = batch.Select((c, i) => (i + 1, NaceBrief(c.Name, c.Industry, c.Description, c.AiAnalysis))).ToList();
            Dictionary<int, string> codes;
            try
            {
                codes = await _naceAi.ClassifyNaceAsync(items, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Yapay zeka kesintisi: kodu bos kalanlar "belirlenemedi" sayilmaz (yanlislikla elenmesin).
                return new NaceAssignResult(filled, unresolved);
            }

            for (var i = 0; i < batch.Length; i++)
            {
                codes.TryGetValue(i + 1, out var raw);
                var code = NaceCatalog.Normalize(raw);
                if (code is null || !NaceCatalog.Exists(code)) { unresolved.Add(batch[i]); continue; }   // uydurma kod = belirlenemedi
                batch[i].NaceCode = code;
                filled++;
            }
        }
        return new NaceAssignResult(filled, unresolved);
    }

    /// <summary>
    /// 4 ya da 6 haneli NACE aramasinda: aranan kodun sinifindaki (ilk 4 hane) resmi alt faaliyetler arasindan firmanin 6 haneli
    /// kodu secilir ve firmaya yazilir (firmanin eski/serbest tahmin kodu kullanilmaz). Yapay zeka sadece resmi
    /// listeden secer. Donen: secim yapilamayan (hicbiri uymayan) firmalar; yapay zeka kesintisinde bos doner (kimse elenmesin).
    /// </summary>
    public async Task<List<Company>> RefineNaceAsync(IReadOnlyList<Company> companies, string targetCode, CancellationToken ct = default)
    {
        var unresolved = new List<Company>();
        var digits = new string(targetCode.Where(char.IsDigit).ToArray());
        if (digits.Length < 4 || companies.Count == 0) return unresolved;

        // Aranan kodun sinifindaki (ilk 4 hane) tum resmi alt faaliyetler secenek olur: firma baska bir alt koda duserse de
        // dogru kod secilir (sonra aranan kodla karsilastirilir).
        var options = NaceCatalog.ActivitiesUnder(digits[..4]);
        if (options.Count == 0) return unresolved;
        var valid = options.Select(o => o.Key).ToHashSet();

        foreach (var batch in companies.Chunk(NaceBatchSize))
        {
            var items = batch.Select((c, i) => (i + 1, NaceBrief(c.Name, c.Industry, c.Description, c.AiAnalysis))).ToList();
            Dictionary<int, string> chosen;
            try
            {
                chosen = await _naceAi.ChooseNaceAsync(items, options, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new List<Company>();
            }

            for (var i = 0; i < batch.Length; i++)
            {
                chosen.TryGetValue(i + 1, out var raw);
                var code = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
                if (!valid.Contains(code)) { unresolved.Add(batch[i]); continue; }   // listede olmayan kod = secim yok
                batch[i].NaceCode = NaceCatalog.FormatSixDigit(code);
            }
        }
        return unresolved;
    }

    /// <summary>Toplu NACE atamasinin sonucu: atanan sayisi ve yapay zekanin denedigi ama kod cikaramadigi firmalar.</summary>
    public record NaceAssignResult(int Filled, List<Company> Unresolved);

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

    /// <summary>"Biz ne arıyoruz?" profili ve puanlama baglami (profil + hedef unvanlar), ornek basina bir kez okunur.</summary>
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
            var proposed = NaceCatalog.Normalize(analysis.NaceCode);
            if (NaceCatalog.Exists(proposed)) company.NaceCode = proposed;
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
