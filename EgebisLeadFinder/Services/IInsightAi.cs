using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Faz-II rapor yapay zekasi: iki firmayi karsilastirma, NACE sektor analizi ve sirket
/// sitesinden ICP onerisi. Tumu "Biz ne arıyoruz?" sirket profiline gore degerlendirir.
/// </summary>
public interface IInsightAi
{
    Task<CompareAiResult> CompareCompaniesAsync(BusinessProfile? profile, string companyA, string companyB, CancellationToken ct = default);
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
