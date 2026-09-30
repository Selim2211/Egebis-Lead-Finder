using System.Text.Json;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EgebisLeadFinder.Services;

/// <summary>Anahtar kelimeyle eslesen NACE kodu: katalogdan veya yapay zekadan.</summary>
public record NaceMatch(string Code, string Name, string? Reason, bool FromAi, int Companies);

/// <summary>
/// Faz-II madde 4: NACE bazinda sektor analizi. Kullanici NACE kodu bilmek zorunda degil:
/// anahtar kelime yazar, uyan kodlar listelenir (katalog + yapay zeka), secilenler sirket
/// profilimize gore yapay zeka ile degerlendirilip rapor olarak kaydedilir.
/// </summary>
public class SectorAnalysisService
{
    public const int MaxSectors = 6;
    private static readonly TimeSpan MatchCacheTime = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ApplicationDbContext _db;
    private readonly IInsightAi _ai;
    private readonly BusinessProfileService _profiles;
    private readonly IMemoryCache _cache;

    public SectorAnalysisService(ApplicationDbContext db, IInsightAi ai, BusinessProfileService profiles, IMemoryCache cache)
    {
        _db = db;
        _ai = ai;
        _profiles = profiles;
        _cache = cache;
    }

    /// <summary>
    /// Katalogdaki bolum adlarinda gecen kelimeler (Turkce karakterden bagimsiz, kelime basi).
    /// Kod yazildiysa ("22" / "22.22") dogrudan o kod.
    /// </summary>
    public static List<NaceDivision> LocalMatches(string keyword)
    {
        var code = NaceCatalog.Normalize(keyword);
        if (code is not null && keyword.Trim().All(ch => char.IsDigit(ch) || ch == '.'))
            return NaceCatalog.Division(code) is { } d ? new List<NaceDivision> { d } : new List<NaceDivision>();

        var words = TurkishText.Normalize(keyword)
            .Split(new[] { ' ', ',', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3)
            .Select(w => w.Length > 5 ? w[..5] : w) // kaba kok: "plastik" -> "plast", "otomotiv" -> "otomo"
            .Where(w => !GenericWords.Contains(w))
            .Distinct()
            .ToList();
        if (words.Count == 0) return new List<NaceDivision>();

        // Daha cok kelimesi tutan bolum once; genel kelimeler ("urun", "imalat") eslemeye katilmaz.
        return NaceCatalog.Divisions
            .Select(d =>
            {
                var name = " " + TurkishText.Normalize(d.Name);
                return (Division: d, Hits: words.Count(w => name.Contains(" " + w, StringComparison.Ordinal)));
            })
            .Where(x => x.Hits > 0)
            .OrderByDescending(x => x.Hits)
            .ThenBy(x => x.Division.Code, StringComparer.Ordinal)
            .Take(8)
            .Select(x => x.Division)
            .ToList();
    }

    /// <summary>Hemen her bolum adinda gecen, tek basina sektor anlatmayan kelime kokleri.</summary>
    private static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
    {
        "urun", "urunl", "imala", "ureti", "urete", "hizme", "faali", "sanay", "sekto", "firma", "sirke", "diger", "ilgil", "ve"
    };

    /// <summary>Katalog + yapay zeka eslesmeleri; yapay zeka sonucu 24 saat onbellekte.</summary>
    public async Task<(List<NaceMatch> Matches, string? AiError)> MatchAsync(string keyword, CancellationToken ct = default)
    {
        keyword = keyword.Trim();
        var key = "nace-match:" + TurkishText.Normalize(keyword);

        if (!_cache.TryGetValue(key, out NaceMatchAiResult? ai) || ai is null)
        {
            ai = await _ai.MatchNaceAsync(keyword, ct);
            if (ai.Error is null) _cache.Set(key, ai, MatchCacheTime);
        }

        var list = new List<(string Code, string Name, string? Reason, bool FromAi)>();
        foreach (var s in ai.Codes)
            list.Add((s.Code, s.Name, s.Reason, true));
        foreach (var d in LocalMatches(keyword))
            if (list.All(x => x.Code != d.Code))
                list.Add((d.Code, d.Name, "Bölüm adında geçiyor", false));

        var counts = await CountsAsync(list.Select(x => x.Code), ct);
        var matches = list.Select(x => new NaceMatch(x.Code, x.Name, x.Reason, x.FromAi, counts.GetValueOrDefault(x.Code))).ToList();
        return (matches, ai.Error);
    }

    private async Task<Dictionary<string, int>> CountsAsync(IEnumerable<string> codes, CancellationToken ct)
    {
        var result = new Dictionary<string, int>();
        foreach (var code in codes.Distinct())
            result[code] = await _db.Companies.CountAsync(c => c.NaceCode != null && c.NaceCode.StartsWith(code), ct);
        return result;
    }

    /// <summary>Uygulamada bu NACE koduyla kayitli firmalarin ozeti.</summary>
    public async Task<SectorStats> StatsAsync(string code, CancellationToken ct = default)
    {
        var q = _db.Companies.AsNoTracking().Where(c => c.NaceCode != null && c.NaceCode.StartsWith(code));
        var row = await q.GroupBy(_ => 1).Select(g => new
        {
            Count = g.Count(),
            AvgScore = g.Average(c => (double)c.Score),
            AvgFit = g.Average(c => (double?)c.FitScore),
            Icp = g.Count(c => c.IcpMatch),
            Projects = g.Count(c => c.ProjectStartedAt != null)
        }).FirstOrDefaultAsync(ct);

        if (row is null) return new SectorStats(0, 0, null, 0, 0, 0);
        var leads = await _db.Leads.CountAsync(l => l.Company.NaceCode != null && l.Company.NaceCode.StartsWith(code), ct);
        return new SectorStats(row.Count, (int)Math.Round(row.AvgScore), row.AvgFit is double f ? (int)Math.Round(f) : null,
            row.Icp, leads, row.Projects);
    }

    /// <summary>"kod|ad" bicimindeki secimleri cozer; gecersiz ve tekrar eden kodlar atilir.</summary>
    public static List<(string Code, string Name)> ParseSelection(IEnumerable<string>? values)
    {
        var result = new List<(string, string)>();
        foreach (var raw in values ?? Enumerable.Empty<string>())
        {
            var parts = raw.Split('|', 2);
            var code = NaceCatalog.Normalize(parts[0]);
            if (code is null || NaceCatalog.Division(code) is null || result.Any(x => x.Item1 == code)) continue;
            var name = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : NaceCatalog.Division(code)!.Name;
            result.Add((code, name.Length > 150 ? name[..150] : name));
        }
        return result;
    }

    /// <summary>Secilen sektorleri analiz eder ve raporu kaydeder. Hata olursa rapor kaydedilmez.</summary>
    public async Task<(SectorReport? Report, string? Error)> AnalyzeAsync(string keyword, IReadOnlyList<(string Code, string Name)> selection,
        int? userId, string? userName, CancellationToken ct = default)
    {
        if (selection.Count == 0) return (null, "En az bir NACE kodu seçin.");
        if (selection.Count > MaxSectors) return (null, $"En fazla {MaxSectors} sektör seçebilirsiniz.");

        var inputs = new List<SectorInput>();
        foreach (var (code, name) in selection)
            inputs.Add(new SectorInput(code, name, await StatsAsync(code, ct)));

        var profile = await _profiles.GetAsync(ct);
        var result = await _ai.AnalyzeSectorsAsync(profile, keyword, inputs, ct);
        if (!result.Success) return (null, result.Error);

        var top = result.Sectors.OrderByDescending(s => s.FitScore).FirstOrDefault();
        var report = new SectorReport
        {
            UserId = userId,
            UserName = userName,
            Keyword = keyword.Length > 150 ? keyword[..150] : keyword,
            Codes = string.Join(",", selection.Select(s => s.Code)),
            TopSector = top is null ? null : Truncate($"{top.Code} · {top.Name}", 200),
            TopScore = top?.FitScore,
            ResultJson = JsonSerializer.Serialize(result)
        };
        _db.SectorReports.Add(report);
        await _db.SaveChangesAsync(ct);
        return (report, null);
    }

    public static SectorAiResult Parse(SectorReport report)
    {
        try
        {
            return JsonSerializer.Deserialize<SectorAiResult>(report.ResultJson, Json) ?? new SectorAiResult();
        }
        catch (JsonException)
        {
            return new SectorAiResult { Error = "Rapor okunamadı." };
        }
    }

    /// <summary>Raporlar kisiye ozeldir; yonetici hepsini gorur.</summary>
    public IQueryable<SectorReport> Visible(int? userId, bool isAdmin) =>
        isAdmin ? _db.SectorReports.AsNoTracking() : _db.SectorReports.AsNoTracking().Where(r => r.UserId == userId);

    public static string VerdictLabel(string verdict) => verdict switch
    {
        "cok_uygun" => "Çok uygun",
        "uygun" => "Uygun",
        "uygun_degil" => "Uygun değil",
        _ => "Kısmen uygun"
    };

    /// <summary>Uygunluk rozeti rengi: FitDisplay ile ayni esikler.</summary>
    public static string VerdictCss(int score) => FitDisplay.Css(score);

    /// <summary>Excel: ozet sayfasi + sektor basina ayrinti.</summary>
    public static List<ExportTable> ToExport(SectorReport report, SectorAiResult result)
    {
        static string Lines(IEnumerable<string> items) => string.Join("\n", items.Select(i => "• " + i));

        var summary = result.Sectors.Select(s => new object?[]
        {
            s.Code, s.Name, s.FitScore, VerdictLabel(s.Verdict), s.Summary,
            s.Stats?.Companies, s.Stats?.AvgScore, s.Stats?.AvgFit, s.Stats?.Leads
        }).ToList();

        var detail = result.Sectors.Select(s => new object?[]
        {
            s.Code, s.Name, Lines(s.Reasons), Lines(s.Risks), Lines(s.Needs), s.IdealCompany,
            string.Join(", ", s.SearchTerms), string.Join(", ", s.Titles)
        }).ToList();

        var info = new List<object?[]>
        {
            new object?[] { "Anahtar kelime", report.Keyword },
            new object?[] { "Tarih", report.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm") },
            new object?[] { "Hazırlayan", report.UserName },
            new object?[] { "Genel değerlendirme", result.Overview }
        };

        return new List<ExportTable>
        {
            new("Özet", new[] { "NACE", "Sektör", "Uygunluk", "Değerlendirme", "Özet", "Kayıtlı firma", "Ort. lead puanı", "Ort. uygunluk %", "Lead" }, summary),
            new("Ayrıntı", new[] { "NACE", "Sektör", "Neden uygun", "Riskler", "Çözdüğümüz ihtiyaçlar", "Hedef firma profili", "Arama terimleri", "Ulaşılacak unvanlar" }, detail),
            new("Rapor", new[] { "Alan", "Değer" }, info)
        };
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;
}
