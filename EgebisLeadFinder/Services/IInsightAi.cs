using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Faz-II rapor yapay zekasi: iki firmayi karsilastirma, NACE sektor analizi ve sirket
/// sitesinden ICP onerisi. Tumu "Biz ne arıyoruz?" sirket profiline gore degerlendirir.
/// </summary>
public interface IInsightAi
{
    Task<CompareAiResult> CompareCompaniesAsync(BusinessProfile? profile, string companyA, string companyB, CancellationToken ct = default);

    /// <summary>Anahtar kelimeye uyan NACE Rev.2 kodlari (bolum "22" veya sinif "22.22").</summary>
    Task<NaceMatchAiResult> MatchNaceAsync(string keyword, CancellationToken ct = default);

    /// <summary>Secilen NACE sektorlerinin sirketimize ne kadar uygun oldugunu degerlendirir.</summary>
    Task<SectorAiResult> AnalyzeSectorsAsync(BusinessProfile? profile, string keyword, IReadOnlyList<SectorInput> sectors, CancellationToken ct = default);
}

public record NaceSuggestion(string Code, string Name, string? Reason);

public class NaceMatchAiResult
{
    public List<NaceSuggestion> Codes { get; set; } = new();
    public string? Error { get; set; }
}

/// <summary>Analize giden sektor: kod, ad ve uygulamadaki firma istatistikleri.</summary>
public record SectorInput(string Code, string Name, SectorStats Stats);

/// <summary>Uygulamada bu NACE koduyla kayitli firmalarin ozeti.</summary>
public record SectorStats(int Companies, int AvgScore, int? AvgFit, int IcpMatches, int Leads, int Projects);

/// <summary>Tek sektorun yapay zeka degerlendirmesi.</summary>
public class SectorAssessment
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>0-100: bu sektor bize ne kadar uygun.</summary>
    public int FitScore { get; set; }

    /// <summary>"cok_uygun", "uygun", "kismen", "uygun_degil".</summary>
    public string Verdict { get; set; } = "kismen";
    public string? Summary { get; set; }
    public List<string> Reasons { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public List<string> Needs { get; set; } = new();
    public string? IdealCompany { get; set; }
    public List<string> SearchTerms { get; set; } = new();
    public List<string> Titles { get; set; } = new();

    /// <summary>Uygulamadaki istatistik (rapor aninda).</summary>
    public SectorStats? Stats { get; set; }
}

public class SectorAiResult
{
    public string? Overview { get; set; }
    public List<SectorAssessment> Sectors { get; set; } = new();
    public string? Error { get; set; }
    public bool Success => Error is null;
}

/// <summary>Iki firmanin yapay zeka karsilastirmasi.</summary>
public class CompareAiResult
{
    /// <summary>"A", "B" veya "esit".</summary>
    public string Recommended { get; set; } = "esit";
    public string? Headline { get; set; }
    public string? Summary { get; set; }
    public List<string> AStrengths { get; set; } = new();
    public List<string> BStrengths { get; set; } = new();
    public List<string> ARisks { get; set; } = new();
    public List<string> BRisks { get; set; } = new();
    public List<string> NextSteps { get; set; } = new();
    public string? Error { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public bool Success => Error is null;
}
