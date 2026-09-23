using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.CompanyIntel;

namespace EgebisLeadFinder.Services;

/// <summary>
/// AI #3: on arastirmada toplanan kaynak parcalarindan yapisal bir firma
/// degerlendirmesi (CompanyRating) uretir. GeminiAiService bu arayuzu de uygular
/// ve mevcut retry altyapisini paylasir.
/// </summary>
public interface ICompanyRatingAi
{
    Task<CompanyRatingAiResult> RateCompanyAsync(CompanyRatingInput input, CancellationToken ct = default);
}

/// <summary>AI'a verilecek girdi: firma + mevcut analiz + toplanan kaynak parcalari.</summary>
public class CompanyRatingInput
{
    public Company Company { get; init; } = null!;
    public CompanyAnalysis? Analysis { get; init; }
    public IReadOnlyList<IntelSnippet> Snippets { get; init; } = new List<IntelSnippet>();
}

/// <summary>AI sonucu ve ham JSON. Ham JSON dogrudan Company.RatingJson (jsonb) kolonuna yazilir.</summary>
public class CompanyRatingAiResult
{
    public CompanyRating? Rating { get; init; }
    public string? RawJson { get; init; }
    public string? Error { get; init; }
    public bool Success => Rating is not null;

    public static CompanyRatingAiResult Failed(string error) => new() { Error = error };
}
