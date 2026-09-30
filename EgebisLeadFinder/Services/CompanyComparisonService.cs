using System.Text;
using System.Text.Json;
using EgebisLeadFinder.Controllers;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EgebisLeadFinder.Services;

public enum CompareSide { None, A, B }

/// <summary>Karsilastirma tablosunun bir satiri; Better, bizim acimizdan onde olan taraf.</summary>
public record CompareRow(string Label, string? A, string? B, CompareSide Better = CompareSide.None);

public record CompareSection(string Title, string? Note, List<CompareRow> Rows);

/// <summary>Iki firmanin kriterlerimize uygunluk ve genel durum karsilastirmasi.</summary>
public class CompanyComparison
{
    public Company A { get; init; } = null!;
    public Company B { get; init; } = null!;
    public List<CompareSection> Sections { get; } = new();

    /// <summary>Kurallara gore onde olan firma (lead puani, sonra profil uygunlugu).</summary>
    public CompareSide Leader { get; set; }
    public string LeaderReason { get; set; } = string.Empty;

    public int AWins => Sections.SelectMany(s => s.Rows).Count(r => r.Better == CompareSide.A);
    public int BWins => Sections.SelectMany(s => s.Rows).Count(r => r.Better == CompareSide.B);

    /// <summary>"Kriterlerimiz" kutusu: sirket profili ve ICP ozeti.</summary>
    public List<string> Criteria { get; } = new();

    public CompareAiResult? Ai { get; set; }

    /// <summary>Yapay zeka yorumu icin iki firmanin metin ozeti (istek aninda doldurulur).</summary>
    internal string BriefA { get; set; } = string.Empty;
    internal string BriefB { get; set; } = string.Empty;

    public Company Side(CompareSide side) => side == CompareSide.B ? B : A;
}

/// <summary>
/// Faz-II madde 3: secilen iki firmayi Ayarlar'daki kriterlerimize (sirket profili + ICP) uygunluk
/// ve genel durum acisindan karsilastirir; istege bagli yapay zeka yorumu ekler, Excel'e aktarir.
/// </summary>
public class CompanyComparisonService
{
    private static readonly TimeSpan AiCacheTime = TimeSpan.FromHours(12);

    private readonly ApplicationDbContext _db;
    private readonly IcpService _icp;
    private readonly BusinessProfileService _profiles;
    private readonly IInsightAi _ai;
    private readonly IMemoryCache _cache;

    public CompanyComparisonService(ApplicationDbContext db, IcpService icp, BusinessProfileService profiles,
        IInsightAi ai, IMemoryCache cache)
    {
        _db = db;
        _icp = icp;
        _profiles = profiles;
        _ai = ai;
        _cache = cache;
    }

    public async Task<CompanyComparison?> BuildAsync(int idA, int idB, CancellationToken ct = default)
    {
        if (idA == idB) return null;

        var companies = await _db.Companies.AsNoTracking()
            .Include(c => c.Contacts)
            .Include(c => c.Leads)
            .Where(c => c.Id == idA || c.Id == idB)
            .AsSplitQuery()
            .ToListAsync(ct);

        var a = companies.FirstOrDefault(c => c.Id == idA);
        var b = companies.FirstOrDefault(c => c.Id == idB);
        if (a is null || b is null) return null;

        var profile = await _profiles.GetAsync(ct);
        var icp = await _icp.GetAsync(ct);
        var analysisA = CompanyController.ParseAnalysis(a.AiAnalysis);
        var analysisB = CompanyController.ParseAnalysis(b.AiAnalysis);
        var breakdownA = await _icp.BreakdownAsync(a, analysisA, ct);
        var breakdownB = await _icp.BreakdownAsync(b, analysisB, ct);
        var ratingA = ParseRating(a.RatingJson);
        var ratingB = ParseRating(b.RatingJson);

        var result = new CompanyComparison { A = a, B = b };
        FillCriteria(result, profile, icp);

        // ---- 1. Kriterlerimize uygunluk ----
        var fit = new List<CompareRow>
        {
            Number("Lead puanı (100 üzerinden)", a.Score, b.Score),
            Number("Şirket profilimize uygunluk (%)", a.FitScore, b.FitScore),
            new("Uyduğu hedef segment", a.FitSegment ?? "—", b.FitSegment ?? "—"),
            Flag("İdeal müşteri profiline (ICP) uyuyor", a.IcpMatch, b.IcpMatch),
            new("Uygunluk gerekçesi (yapay zekâ)", a.FitReason ?? analysisA?.Reason ?? "—", b.FitReason ?? analysisB?.Reason ?? "—"),
            new("Değerlendirme durumu", Evaluation(a, breakdownA), Evaluation(b, breakdownB),
                Better(Rank(a, breakdownA), Rank(b, breakdownB)))
        };
        result.Sections.Add(new CompareSection("Kriterlerimize uygunluk",
            "Ayarlar'daki şirket profili (Biz ne arıyoruz?) ve ideal müşteri profiline göre", fit));

        // ---- 2. Puan kalemleri: iki firmanin puanini olusturan kriterler yan yana ----
        var reasons = breakdownA.Items.Select(i => i.Reason)
            .Concat(breakdownB.Items.Select(i => i.Reason))
            .Distinct()
            .ToList();
        var items = reasons.Select(r =>
        {
            var pa = breakdownA.Items.Where(i => i.Reason == r).Sum(i => i.Points);
            var pb = breakdownB.Items.Where(i => i.Reason == r).Sum(i => i.Points);
            return new CompareRow(r, pa > 0 ? $"+{pa}" : "—", pb > 0 ? $"+{pb}" : "—", Better(pa, pb));
        }).ToList();
        if (items.Count > 0)
            result.Sections.Add(new CompareSection("Puan kalemleri", "Lead puanını oluşturan kriterler", items));

        // ---- 3. Firma profili ----
        var general = new List<CompareRow>
        {
            new("Sektör", a.Industry ?? "—", b.Industry ?? "—"),
            new("NACE", NaceCatalog.Label(a.NaceCode) ?? "—", NaceCatalog.Label(b.NaceCode) ?? "—"),
            new("Konum", Location(a), Location(b)),
            Number("Çalışan sayısı (tahmini)", Employees(analysisA), Employees(analysisB),
                analysisA?.EmployeeSizeHint, analysisB?.EmployeeSizeHint),
            Flag("Üretici firma", analysisA?.Manufacturer, analysisB?.Manufacturer),
            new("Ürünler / hizmetler", Join(analysisA?.Products), Join(analysisB?.Products)),
            new("ERP / SAP durumu", SapLabel(analysisA), SapLabel(analysisB))
        };
        result.Sections.Add(new CompareSection("Firma profili", "Firma sitesinin yapay zekâ analizinden", general));

        // ---- 4. Genel durum (internet on arastirmasi) ----
        var status = new List<CompareRow>
        {
            new("Ön araştırma sinyali", SignalLabel(a), SignalLabel(b), Better(SignalRank(a.RatingSignal), SignalRank(b.RatingSignal))),
            new("Özet", ratingA?.Summary ?? "—", ratingB?.Summary ?? "—"),
            new("Ölçek", ratingA?.ScaleInfo ?? "—", ratingB?.ScaleInfo ?? "—"),
            new("Finansal bilgi", ratingA?.FinancialInfo ?? "—", ratingB?.FinancialInfo ?? "—"),
            new("Kuruluş", ratingA?.FoundingInfo ?? "—", ratingB?.FoundingInfo ?? "—")
        };
        result.Sections.Add(new CompareSection("Genel durum", "Firma analizi (internet ön araştırması)", status));

        // ---- 5. Satis durumu ----
        var sales = new List<CompareRow>
        {
            Number("Bulunan kişi", a.Contacts.Count, b.Contacts.Count),
            Number("E-postası olan kişi", a.Contacts.Count(c => !string.IsNullOrWhiteSpace(c.Email)),
                b.Contacts.Count(c => !string.IsNullOrWhiteSpace(c.Email))),
            new("Lead", a.Leads.Count == 0 ? "Yok" : $"{a.Leads.Count} lead", b.Leads.Count == 0 ? "Yok" : $"{b.Leads.Count} lead"),
            new("Satış aşaması", Stage(a), Stage(b)),
            new("Kayıt tarihi", a.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy"), b.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy"))
        };
        result.Sections.Add(new CompareSection("Satış durumu", null, sales));

        (result.Leader, result.LeaderReason) = DecideLeader(a, b);
        result.BriefA = Brief(a, analysisA, breakdownA, ratingA);
        result.BriefB = Brief(b, analysisB, breakdownB, ratingB);
        result.Ai = _cache.TryGetValue(CacheKey(a, b), out CompareAiResult? cached) ? cached : null;
        return result;
    }

    /// <summary>Yapay zeka yorumunu uretir ve onbellege alir (ayni iki firma icin 12 saat).</summary>
    public async Task<CompareAiResult> GenerateAiAsync(CompanyComparison comparison, CancellationToken ct = default)
    {
        var profile = await _profiles.GetAsync(ct);
        var result = await _ai.CompareCompaniesAsync(profile, comparison.BriefA, comparison.BriefB, ct);
        if (result.Success) _cache.Set(CacheKey(comparison.A, comparison.B), result, AiCacheTime);
        comparison.Ai = result;
        return result;
    }

    /// <summary>Excel: karsilastirma tablosu + (varsa) yapay zeka yorumu.</summary>
    public static List<ExportTable> ToExport(CompanyComparison c)
    {
        string Better(CompareSide side) => side switch
        {
            CompareSide.A => c.A.Name,
            CompareSide.B => c.B.Name,
            _ => ""
        };

        var rows = c.Sections
            .SelectMany(s => s.Rows.Select(r => new object?[] { s.Title, r.Label, r.A, r.B, Better(r.Better) }))
            .ToList();
        rows.Add(new object?[] { "Sonuç", "Kurallara göre önde", c.Leader == CompareSide.None ? "Eşit" : Better(c.Leader), null, c.LeaderReason });

        var tables = new List<ExportTable>
        {
            new("Karşılaştırma", new[] { "Bölüm", "Kriter", c.A.Name, c.B.Name, "Önde" }, rows),
            new("Kriterlerimiz", new[] { "Kriter" }, c.Criteria.Select(x => new object?[] { x }).ToList())
        };

        if (c.Ai is { Success: true } ai)
        {
            var aiRows = new List<object?[]>
            {
                new object?[] { "Öncelik", ai.Recommended switch { "A" => c.A.Name, "B" => c.B.Name, _ => "Eşit" } },
                new object?[] { "Sonuç", ai.Headline },
                new object?[] { "Değerlendirme", ai.Summary }
            };
            aiRows.AddRange(ai.AStrengths.Select(x => new object?[] { $"{c.A.Name} güçlü yanı", x }));
            aiRows.AddRange(ai.BStrengths.Select(x => new object?[] { $"{c.B.Name} güçlü yanı", x }));
            aiRows.AddRange(ai.ARisks.Select(x => new object?[] { $"{c.A.Name} riski", x }));
            aiRows.AddRange(ai.BRisks.Select(x => new object?[] { $"{c.B.Name} riski", x }));
            aiRows.AddRange(ai.NextSteps.Select(x => new object?[] { "Sonraki adım", x }));
            tables.Add(new ExportTable("Yapay zekâ yorumu", new[] { "Başlık", "Metin" }, aiRows));
        }

        return tables;
    }

    // ---------------- yardimcilar ----------------

    private static string CacheKey(Company a, Company b) =>
        $"compare:{a.Id}:{a.Score}:{a.RatedAt?.Ticks}:{b.Id}:{b.Score}:{b.RatedAt?.Ticks}";

    private static void FillCriteria(CompanyComparison result, BusinessProfile profile, IcpProfile icp)
    {
        if (profile.IsConfigured)
        {
            result.Criteria.Add($"Ne satıyoruz: {profile.Offering}");
            result.Criteria.Add($"İdeal müşteri: {profile.IdealCustomer}");
            if (profile.Segments.Count > 0)
                result.Criteria.Add($"Hedef segmentler: {string.Join(", ", profile.Segments.Select(s => s.Name))}");
        }
        else
        {
            result.Criteria.Add("Şirket profili (Biz ne arıyoruz?) tanımlı değil; varsayılan kriterler kullanılıyor.");
        }

        if (icp.IsActive)
        {
            if (icp.NaceCodes.Count > 0) result.Criteria.Add($"ICP sektörleri (NACE): {string.Join(", ", icp.NaceCodes)}");
            if (icp.IndustryKeywords.Count > 0) result.Criteria.Add($"ICP sektör kelimeleri: {string.Join(", ", icp.IndustryKeywords)}");
            if (icp.Cities.Count + icp.Countries.Count > 0)
                result.Criteria.Add($"ICP konum: {string.Join(", ", icp.Cities.Concat(icp.Countries))}");
            if (icp.MinEmployees > 0) result.Criteria.Add($"Asgari çalışan: {icp.MinEmployees}");
            if (icp.RequireManufacturer) result.Criteria.Add("Üretici firma olmalı");
        }
    }

    public static (CompareSide Leader, string Reason) DecideLeader(Company a, Company b)
    {
        if (a.Score != b.Score)
        {
            var leader = a.Score > b.Score ? CompareSide.A : CompareSide.B;
            return (leader, $"Lead puanı daha yüksek ({Math.Max(a.Score, b.Score)} / {Math.Min(a.Score, b.Score)}).");
        }

        if ((a.FitScore ?? -1) != (b.FitScore ?? -1))
        {
            var leader = (a.FitScore ?? -1) > (b.FitScore ?? -1) ? CompareSide.A : CompareSide.B;
            return (leader, "Lead puanları eşit; şirket profilimize uygunluğu daha yüksek.");
        }

        return (CompareSide.None, "Lead puanı ve profil uygunluğu eşit.");
    }

    private static CompareSide Better(double? a, double? b)
    {
        if (a is null && b is null) return CompareSide.None;
        if (a is null) return CompareSide.B;
        if (b is null) return CompareSide.A;
        return a > b ? CompareSide.A : b > a ? CompareSide.B : CompareSide.None;
    }

    private static CompareRow Number(string label, int? a, int? b, string? textA = null, string? textB = null) =>
        new(label, textA ?? a?.ToString() ?? "—", textB ?? b?.ToString() ?? "—", Better(a, b));

    private static CompareRow Flag(string label, bool? a, bool? b) =>
        new(label, YesNo(a), YesNo(b), Better(a is null ? null : a.Value ? 1 : 0, b is null ? null : b.Value ? 1 : 0));

    private static string YesNo(bool? value) => value switch { true => "Evet", false => "Hayır", _ => "—" };

    private static int? Employees(CompanyAnalysis? a)
    {
        var n = LeadScoringService.EmployeeCount(a?.EmployeeSizeHint);
        return n > 0 ? n : null;
    }

    private static string Location(Company c) =>
        string.Join(", ", new[] { c.City, c.Country }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } s ? s : "—";

    private static string Join(IEnumerable<string>? items) =>
        items is null ? "—" : string.Join(", ", items.Where(i => !string.IsNullOrWhiteSpace(i)).Take(8)) is { Length: > 0 } s ? s : "—";

    private static string SapLabel(CompanyAnalysis? a) => a?.Sap switch
    {
        "yes" => "SAP kullanıyor",
        "likely" => "SAP ihtimali yüksek",
        "no" => "SAP kullanmıyor",
        _ => "Bilinmiyor"
    };

    private static int Rank(Company c, ScoreBreakdown b) =>
        c.EvaluationStatus == EvaluationStatus.NotEvaluated ? 0 : b.DisqualifiedReason is not null ? 0 : 1;

    private static string Evaluation(Company c, ScoreBreakdown b) =>
        c.EvaluationStatus == EvaluationStatus.NotEvaluated ? $"İncelenemedi{(c.EvaluationNote is null ? "" : $" — {c.EvaluationNote}")}"
        : b.DisqualifiedReason is not null ? $"Elendi — {b.DisqualifiedReason}"
        : b.Unverified ? "Doğrulanmadı (site analizi yok)"
        : "Değerlendirildi";

    private static string SignalLabel(Company c) =>
        RatingSignalDisplay.HasSignal(c.RatingSignal)
            ? $"{RatingSignalDisplay.Label(c.RatingSignal)}{(c.RatedAt is null ? "" : $" ({c.RatedAt.Value.ToLocalTime():dd.MM.yyyy})")}"
            : "Araştırılmadı";

    private static int? SignalRank(string? signal) => RatingSignalDisplay.Parse(signal) switch
    {
        RatingSignal.Guclu => 3,
        RatingSignal.Incelenmeli => 2,
        RatingSignal.Riskli => 1,
        _ => null
    };

    private static string Stage(Company c) =>
        c.ProjectStartedAt is not null ? "Proje başladı"
        : c.EmailSentAt is not null ? "Mail atıldı"
        : c.ContactedAt is not null ? "İletişim kuruldu"
        : "Temas yok";

    private static CompanyRating? ParseRating(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<CompanyRating>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Yapay zekaya giden firma ozeti (yalnizca uygulamadaki veriler).</summary>
    private static string Brief(Company c, CompanyAnalysis? a, ScoreBreakdown b, CompanyRating? r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ad: {c.Name} ({c.Domain})");
        sb.AppendLine($"Konum: {Location(c)}");
        sb.AppendLine($"Sektör: {c.Industry ?? "bilinmiyor"}; NACE: {NaceCatalog.Label(c.NaceCode) ?? "bilinmiyor"}");
        sb.AppendLine($"Lead puanı: {c.Score}/100; değerlendirme: {Evaluation(c, b)}");
        foreach (var item in b.Items) sb.AppendLine($"  + {item.Points}: {item.Reason}");
        sb.AppendLine($"Şirket profilimize uygunluk: {(c.FitScore is int f ? $"%{f}" : "bilinmiyor")}; segment: {c.FitSegment ?? "yok"}");
        if (c.FitReason is not null) sb.AppendLine($"Uygunluk gerekçesi: {c.FitReason}");
        sb.AppendLine($"ICP'ye uyuyor: {YesNo(c.IcpMatch)}");
        if (a is not null)
        {
            sb.AppendLine($"Üretici: {YesNo(a.Manufacturer)}; çalışan: {a.EmployeeSizeHint ?? "bilinmiyor"}; ERP/SAP: {SapLabel(a)}");
            if (a.Products.Count > 0) sb.AppendLine($"Ürünler: {Join(a.Products)}");
        }
        else
        {
            sb.AppendLine("Site analizi yok.");
        }
        if (r is not null)
        {
            sb.AppendLine($"Ön araştırma sinyali: {SignalLabel(c)}");
            if (r.Summary is not null) sb.AppendLine($"Araştırma özeti: {r.Summary}");
            if (r.ScaleInfo is not null) sb.AppendLine($"Ölçek: {r.ScaleInfo}");
            if (r.FinancialInfo is not null) sb.AppendLine($"Finans: {r.FinancialInfo}");
        }
        else
        {
            sb.AppendLine("İnternet ön araştırması yapılmadı.");
        }
        sb.AppendLine($"Kişiler: {c.Contacts.Count} (e-postalı {c.Contacts.Count(x => !string.IsNullOrWhiteSpace(x.Email))}); lead: {c.Leads.Count}; aşama: {Stage(c)}");
        return sb.ToString();
    }
}
