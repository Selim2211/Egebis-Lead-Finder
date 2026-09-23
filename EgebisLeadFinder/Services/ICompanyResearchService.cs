using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.CompanyIntel;
using EgebisLeadFinder.Services.Progress;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Faz-II firma on arastirmasi orkestratoru. Tum ICompanyIntelSource'lari calistirir,
/// sonuclari ICompanyRatingAi'a verir, CompanyRatingEvaluator'dan gecirir.
/// Arama sirasinda otomatik calismaz: kullanici firma detayinda "On Arastirma Yap"
/// dedigi zaman tetiklenir (CompanyController.Enrich deseni).
/// </summary>
public interface ICompanyResearchService
{
    Task<CompanyResearchResult> ResearchAsync(
        Company company,
        CancellationToken ct = default,
        IProgress<JobStep>? progress = null);
}

public class CompanyResearchResult
{
    /// <summary>AI ciktisi. Nihai sinyal icin <see cref="Evaluation"/> kullanilir.</summary>
    public CompanyRating? Rating { get; init; }

    public RatingEvaluation? Evaluation { get; init; }

    /// <summary>Company.RatingJson (jsonb) kolonuna yazilacak ham AI JSON'i.</summary>
    public string? RawJson { get; init; }

    /// <summary>Her kaynaktan toplananlar (UI'da "kontrol edilen kaynaklar" bloku icin).</summary>
    public List<SourceIntel> Sources { get; init; } = new();

    /// <summary>Tum "kendin ac" linkleri (kaynaklardan birlestirilmis).</summary>
    public List<IntelLink> Links { get; init; } = new();

    public string? Error { get; init; }

    public bool Success => Evaluation is not null && Rating is not null;

    public static CompanyResearchResult Failed(string error, List<SourceIntel>? sources = null) =>
        new() { Error = error, Sources = sources ?? new() };
}
