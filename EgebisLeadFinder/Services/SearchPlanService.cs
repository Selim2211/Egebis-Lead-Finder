using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Caching.Memory;

namespace EgebisLeadFinder.Services;

/// <summary>AI #7-8: akilli arama. GeminiAiService uygular.</summary>
public interface ISearchPlannerAi
{
    /// <summary>Sektor/segment ve sirket tanimindan ek arama terimleri (hedef dilde).</summary>
    Task<List<string>> SuggestSearchTermsAsync(SearchTermRequest request, CancellationToken ct = default);

    /// <summary>Site okunmadan, arama sonucu bilgisiyle hedef disi adaylari isaretler.</summary>
    Task<Dictionary<int, CandidateVerdict>> ScreenCandidatesAsync(CandidateScreenRequest request, CancellationToken ct = default);
}

public record SearchTermRequest(
    string Industry, TargetSegment? Segment, BusinessProfile Profile, string CountryName, string LanguageName, int MaxTerms);

public record CandidateScreenRequest(
    string Industry, TargetSegment? Segment, BusinessProfile Profile, IReadOnlyList<(int Id, string Text)> Candidates);

public record CandidateVerdict(bool Keep, string? Reason);

/// <summary>Arama oncesi hazirlik: kullanilacak terimler ve secili segment.</summary>
public class SearchPlan
{
    public List<string> Terms { get; set; } = new();
    public TargetSegment? Segment { get; set; }
    public BusinessProfile Profile { get; set; } = new();

    /// <summary>Akilli arama acik ve yapay zeka kullanilabiliyor mu (ön eleme yapilacak mi)?</summary>
    public bool Smart { get; set; }

    /// <summary>Terim onerisi yapilamadiysa kullaniciya gosterilecek not.</summary>
    public string? Note { get; set; }
}

/// <summary>On elemede atlanan aday (sonuc ekraninda listelenir, veritabanina yazilmaz).</summary>
public record ScreenedOut(string Domain, string Title, string Reason, string Stage);

public class ScreenResult
{
    public List<SearchResult> Kept { get; set; } = new();
    public List<ScreenedOut> Dropped { get; set; } = new();
    public string? Warning { get; set; }
}

/// <summary>
/// Akilli arama: (1) yazilan sektor ve "Biz ne arıyoruz?" segmentinden bolgenin dilinde ek arama
/// terimleri uretir; (2) bulunan adaylari siteleri okunmadan once eler: once kurallarla (ICP ve
/// segment eleme kelimeleri), sonra tek yapay zeka cagrisiyla toplu olarak. Boylece site okuma ve
/// firma analizi (Gemini) yalnizca gercek adaylara harcanir. Herhangi bir adim basarisiz olursa
/// arama eski haliyle devam eder.
/// </summary>
public class SearchPlanService
{
    /// <summary>Kullanicinin terimi dahil en fazla terim sayisi (Serper kredisini sinirlar).</summary>
    public const int MaxTerms = 4;

    /// <summary>Tek yapay zeka cagrisinda degerlendirilen aday sayisi.</summary>
    public const int ScreenBatchSize = 40;

    public const string StageRule = "kural";
    public const string StageAi = "yapay zekâ";

    private static readonly TimeSpan TermCacheDuration = TimeSpan.FromHours(12);

    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tr"] = "Türkçe", ["de"] = "Almanca", ["en"] = "İngilizce"
    };

    private readonly ISettingsService _settings;
    private readonly ISearchPlannerAi _ai;
    private readonly IcpService _icp;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SearchPlanService> _logger;

    public SearchPlanService(ISettingsService settings, ISearchPlannerAi ai, IcpService icp, IMemoryCache cache, ILogger<SearchPlanService> logger)
    {
        _settings = settings;
        _ai = ai;
        _icp = icp;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Terimleri hazirlar ve criteria.SearchTerms / CollectTarget alanlarini doldurur.</summary>
    public async Task<SearchPlan> PrepareAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        var plan = new SearchPlan { Terms = { criteria.Industry.Trim() } };
        if (criteria.IsNameSearch || string.IsNullOrWhiteSpace(criteria.Industry)) return plan;

        plan.Profile = BusinessProfileService.Parse(await _settings.GetAsync(SettingKeys.BusinessProfile, ct));
        plan.Segment = string.IsNullOrWhiteSpace(criteria.SegmentId)
            ? null
            : plan.Profile.Segments.FirstOrDefault(s => s.Id == criteria.SegmentId);

        if (!criteria.SmartSearch) return plan;

        if (string.IsNullOrWhiteSpace(await _settings.GetAsync(SettingKeys.GeminiApiKey, ct)))
        {
            plan.Note = "Akıllı arama için Gemini anahtarı gerekli; arama yalnızca yazılan sektörle yapıldı.";
            return plan;
        }

        plan.Smart = true;
        criteria.CollectTarget = CollectTargetFor(criteria.MaxCompanies);

        var region = SearchRegions.Get(criteria.RegionKey ?? SearchRegions.ByCountry(criteria.Country)?.Key);
        var language = LanguageNames.GetValueOrDefault(region.PackCode, "İngilizce");
        var cacheKey = $"search-terms:{criteria.Industry.Trim().ToLowerInvariant()}|{region.PackCode}|{plan.Segment?.Id}|{plan.Profile.UpdatedAt:O}";

        try
        {
            if (!_cache.TryGetValue(cacheKey, out List<string>? suggested) || suggested is null)
            {
                suggested = await _ai.SuggestSearchTermsAsync(
                    new SearchTermRequest(criteria.Industry.Trim(), plan.Segment, plan.Profile, region.Country, language, MaxTerms - 1), ct);
                _cache.Set(cacheKey, suggested, TermCacheDuration);
            }
            plan.Terms = MergeTerms(criteria.Industry, plan.Segment, suggested);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Terim onerisi yardimci bir adim: basarisizsa kullanicinin terimiyle devam edilir.
            _logger.LogWarning(ex, "Arama terimi önerisi alınamadı.");
            plan.Note = "Ek arama terimleri üretilemedi; yalnızca yazılan sektörle arandı.";
        }

        criteria.SearchTerms = plan.Terms.Skip(1).ToList();
        return plan;
    }

    /// <summary>On eleme payi: elenenlerin yerine aday kalsin diye %50 fazla (en az 5, en fazla 50) toplanir.</summary>
    public static int CollectTargetFor(int max) =>
        Math.Min(max + Math.Clamp(max / 2, 5, 50), SettingKeys.SearchMaxCompaniesUpperLimit + 50);

    /// <summary>Kullanicinin terimi basta; segment arama terimi ve AI onerileri; tekrarsiz, en fazla MaxTerms.</summary>
    public static List<string> MergeTerms(string industry, TargetSegment? segment, IEnumerable<string> suggested)
    {
        var terms = new List<string> { industry.Trim() };
        if (segment is not null) terms.Add(segment.EffectiveSearchTerm);
        terms.AddRange(suggested);

        return terms
            .Select(t => t.Trim().Trim('"'))
            .Where(t => t.Length is > 1 and <= 60)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where((t, i) => i == 0 || !string.Equals(TurkishText.Normalize(t), TurkishText.Normalize(industry), StringComparison.Ordinal))
            .Take(MaxTerms)
            .ToList();
    }

    /// <summary>Siteler okunmadan once hedef disi adaylari eler (kurallar + toplu yapay zeka).</summary>
    public async Task<ScreenResult> ScreenAsync(List<SearchResult> candidates, SearchCriteria criteria, SearchPlan plan, CancellationToken ct = default)
    {
        var result = new ScreenResult();
        if (candidates.Count == 0) return result;

        var icp = await _icp.GetAsync(ct);
        var excludes = icp.ExcludeKeywords
            .Concat(plan.Segment?.ExcludeKeywords ?? new List<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var afterRules = new List<SearchResult>();
        foreach (var c in candidates)
        {
            var hit = RuleHit(c, excludes);
            if (hit is null) afterRules.Add(c);
            else result.Dropped.Add(new ScreenedOut(c.Domain, c.Title, $"eleme kelimesi: {hit}", StageRule));
        }

        if (!plan.Smart)
        {
            result.Kept = afterRules;
            return result;
        }

        var kept = new List<SearchResult>();
        foreach (var batch in afterRules.Chunk(ScreenBatchSize))
        {
            ct.ThrowIfCancellationRequested();
            var items = batch.Select((c, i) => (Id: i + 1, Text: Describe(c))).ToList();

            Dictionary<int, CandidateVerdict> verdicts;
            try
            {
                verdicts = await _ai.ScreenCandidatesAsync(
                    new CandidateScreenRequest(criteria.Industry, plan.Segment, plan.Profile, items), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // On eleme yapilamazsa kimse elenmez: yanlislikla hedef kaybetmektense fazla site okunur.
                _logger.LogWarning(ex, "Ön eleme yapılamadı; adaylar elenmeden işlenecek.");
                result.Warning ??= "Ön eleme yapılamadı (yapay zekâ yanıt vermedi); bulunan tüm adaylar incelendi.";
                kept.AddRange(batch);
                continue;
            }

            for (var i = 0; i < batch.Length; i++)
            {
                if (verdicts.TryGetValue(i + 1, out var v) && !v.Keep)
                    result.Dropped.Add(new ScreenedOut(batch[i].Domain, batch[i].Title, v.Reason ?? "hedef dışı", StageAi));
                else
                    kept.Add(batch[i]);
            }
        }

        result.Kept = kept;
        return result;
    }

    /// <summary>Eleme kelimesi firma basliginda veya Haritalar kategorisinde geciyor mu? (ozet taranmaz: "bayilerimiz" gibi ifadeler uretici sitelerinde de gecer)</summary>
    public static string? RuleHit(SearchResult c, IReadOnlyList<string> excludes)
    {
        // "Sample-Doku" basligi "sample doku" kuralina da takilsin.
        var title = c.Title?.Replace('-', ' ');
        var category = c.Category?.Replace('-', ' ');
        return excludes.FirstOrDefault(e => TurkishText.ContainsWord(title, e) || TurkishText.ContainsWord(category, e));
    }

    public static string Describe(SearchResult c)
    {
        var parts = new List<string> { Clip(c.Title, 120), c.Domain };
        if (!string.IsNullOrWhiteSpace(c.Category)) parts.Add("Kategori: " + Clip(c.Category, 80));
        if (!string.IsNullOrWhiteSpace(c.Address)) parts.Add("Adres: " + Clip(c.Address, 80));
        if (!string.IsNullOrWhiteSpace(c.Snippet)) parts.Add("Özet: " + Clip(c.Snippet, 220));
        return string.Join(" | ", parts);
    }

    private static string Clip(string? value, int max)
    {
        var v = (value ?? string.Empty).Replace('\n', ' ').Trim();
        return v.Length > max ? v[..max] + "…" : v;
    }
}
