using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

public interface IAiService
{
    /// <summary>
    /// AI #1: Firma web sitesi metninden yapisal analiz uretir.
    /// Hata durumunda null doner; akis AI olmadan da devam edebilmelidir.
    /// </summary>
    Task<AiAnalysisResult> AnalyzeCompanyAsync(string siteText, CancellationToken ct = default);
}

/// <summary>
/// Analiz sonucu ve ham JSON. Ham JSON dogrudan Company.AiAnalysis (jsonb) kolonuna yazilir.
/// </summary>
public class AiAnalysisResult
{
    public CompanyAnalysis? Analysis { get; init; }
    public string? RawJson { get; init; }
    public string? Error { get; init; }
    public bool Success => Analysis is not null;

    public static AiAnalysisResult Failed(string error) => new() { Error = error };
}
