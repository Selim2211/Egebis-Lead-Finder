using System.Text.Json;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.CompanyIntel;
using EgebisLeadFinder.Services.Progress;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <inheritdoc />
public class CompanyResearchService : ICompanyResearchService
{
    private readonly IEnumerable<ICompanyIntelSource> _sources;
    private readonly ICompanyRatingAi _ai;
    private readonly CompanyRatingEvaluator _evaluator;
    private readonly ResearchOptions _options;
    private readonly ILogger<CompanyResearchService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CompanyResearchService(
        IEnumerable<ICompanyIntelSource> sources,
        ICompanyRatingAi ai,
        CompanyRatingEvaluator evaluator,
        IOptions<ResearchOptions> options,
        ILogger<CompanyResearchService> logger)
    {
        _sources = sources;
        _ai = ai;
        _evaluator = evaluator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CompanyResearchResult> ResearchAsync(
        Company company,
        CancellationToken ct = default,
        IProgress<JobStep>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(company.Name))
            return CompanyResearchResult.Failed("Firma adı yok, araştırma yapılamaz.");

        // 1) Tum kaynaklari es zamanli calistir (LeadDiscoveryService throttle deseni).
        using var throttle = new SemaphoreSlim(Math.Max(1, _options.MaxParallelSources));

        var sourceList = _sources.ToList();
        var sourcesDone = 0;

        progress?.Report(new JobStep(5, "Kamuya açık kaynaklar taranıyor",
            "haber, KAP, resmi kayıtlar"));

        var collected = await Task.WhenAll(sourceList.Select(async source =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return await source.CollectAsync(company, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kaynak başarısız: {Source}", source.Name);
                var failed = SourceIntel.Empty(source.Name);
                failed.Error = ex.Message;
                return failed;
            }
            finally
            {
                throttle.Release();

                // Kaynaklar paralel; ilerleme tamamlanan sayaci uzerinden 5-65 araligina yayilir.
                var n = Interlocked.Increment(ref sourcesDone);
                progress?.Report(new JobStep(
                    5 + (int)(60.0 * n / Math.Max(1, sourceList.Count)),
                    "Kamuya açık kaynaklar taranıyor",
                    $"{n}/{sourceList.Count} kaynak tarandı"));
            }
        }));

        var sources = collected.ToList();
        var snippets = sources.SelectMany(s => s.Snippets).ToList();
        var links = sources.SelectMany(s => s.Links)
            .GroupBy(l => l.Url)
            .Select(g => g.First())
            .ToList();

        if (snippets.Count == 0)
        {
            // Kaynak yok: sinyal uretilemez ama "kendin ac" linkleri yine sunulur.
            var emptyRating = EmptyRating(sources);
            emptyRating.RegistryLinks = links.Select(l => new RatingLink { Label = l.Label, Url = l.Url }).ToList();
            emptyRating.EvaluatorNotes = new List<string> { "İnternette firma hakkında yeterli bilgi bulunamadı." };
            emptyRating.EvaluatorNotesV2 = new List<EvaluatorNote>
            {
                new() { Reason = "İnternette firma hakkında yeterli bilgi bulunamadı.", Direction = "bilinmiyor" }
            };
            emptyRating.SourceSnippets = BuildSourceSnippets(sources);
            emptyRating.Confidence = 0;
            emptyRating.ConfidenceInputs = new ConfidenceInputs();

            return new CompanyResearchResult
            {
                Rating = emptyRating,
                Evaluation = new RatingEvaluation
                {
                    Signal = RatingSignal.Bilinmiyor,
                    Items = { ("İnternette firma hakkında yeterli bilgi bulunamadı", RatingSignal.Bilinmiyor) }
                },
                RawJson = ReSerialize(emptyRating, null),
                Sources = sources,
                Links = links
            };
        }

        // 2) AI sentezi.
        progress?.Report(new JobStep(72, "Yapay zeka finansal analizi yapıyor",
            $"{snippets.Count} bilgi parçası değerlendiriliyor"));

        var aiResult = await _ai.RateCompanyAsync(new CompanyRatingInput
        {
            Company = company,
            Analysis = ParseAnalysis(company.AiAnalysis),
            Snippets = snippets
        }, ct);

        // AI patlarsa (ör. Gemini 503 "yogun talep") toplanan kaynaklari (6-7 Serper
        // kredisi harcanarak toplandi) copa atmayalim: ham parcalarla kismi bir kayit
        // uretip kaydediyoruz, kullanici drill-down'dan kaynaklari yine gorebiliyor ve
        // Serper kredisi bosa gitmiyor. Nihai sinyal AI yorumu olmadan da uretilebilir
        // (CompanyRatingEvaluator deterministik, bkz. Evaluate).
        var rating = aiResult.Success
            ? aiResult.Rating!
            : new CompanyRating
            {
                Signal = "incelenmeli",
                Summary = $"AI değerlendirmesi yapılamadı ({aiResult.Error}). " +
                          "Aşağıdaki ham kaynaklarla elle değerlendirin veya birkaç dakika sonra tekrar deneyin.",
                FinancialSource = "yok"
            };

        // Kaynak durumunu AI ciktisina isle (seffaflik).
        rating.CheckedSources = sources.Select(s => new CheckedSource
        {
            Name = s.SourceName,
            FoundSomething = s.FoundSomething
        }).ToList();

        // 3) Deterministik degerlendirme.
        progress?.Report(new JobStep(95, "Değerlendirme derleniyor"));

        var evaluation = _evaluator.Evaluate(rating, snippets);
        rating.Signal = SignalToJson(evaluation.Signal);
        // Duz liste geriye uyum icin korunur; V2 yon bilgisini de tasir.
        rating.EvaluatorNotes = evaluation.Items.Select(i => i.Reason).ToList();
        rating.EvaluatorNotesV2 = evaluation.Items
            .Select(i => new EvaluatorNote { Reason = i.Reason, Direction = SignalToJson(i.Direction) })
            .ToList();
        rating.RegistryLinks = links.Select(l => new RatingLink { Label = l.Label, Url = l.Url }).ToList();
        rating.SourceSnippets = BuildSourceSnippets(sources);
        rating.Confidence = evaluation.Confidence;
        rating.ConfidenceInputs = new ConfidenceInputs
        {
            SourceCount = evaluation.SourceCount,
            SnippetCount = evaluation.SnippetCount,
            PositiveCount = evaluation.PositiveCount
        };

        return new CompanyResearchResult
        {
            Rating = rating,
            Evaluation = evaluation,
            RawJson = ReSerialize(rating, aiResult.RawJson),
            Sources = sources,
            Links = links
        };
    }

    private static CompanyRating EmptyRating(List<SourceIntel> sources) => new()
    {
        Signal = "incelenmeli",
        Summary = "İnternette firma hakkında yeterli bilgi bulunamadı.",
        CheckedSources = sources.Select(s => new CheckedSource
        {
            Name = s.SourceName,
            FoundSomething = false
        }).ToList()
    };

    /// <summary>Her kaynagin ilk birkac ham parcasini drill-down icin kompakt sekle cevirir.</summary>
    private static List<SourceSnippetGroup> BuildSourceSnippets(List<SourceIntel> sources) =>
        sources.Select(s => new SourceSnippetGroup
        {
            Name = s.SourceName,
            FoundSomething = s.FoundSomething,
            Error = s.Error,
            Snippets = s.Snippets
                .Take(5)
                .Select(sn => new SourceSnippetItem
                {
                    Text = Truncate(sn.Text, 280),
                    Url = sn.SourceUrl,
                    Kind = sn.Kind.ToString()
                })
                .ToList()
        }).ToList();

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max].TrimEnd() + "…";
    }

    private static string SignalToJson(RatingSignal signal) => signal switch
    {
        RatingSignal.Guclu => "guclu",
        RatingSignal.Riskli => "riskli",
        RatingSignal.Bilinmiyor => "bilinmiyor",
        _ => "incelenmeli"
    };

    /// <summary>Evaluator'in ezdigi signal alanini iceren guncel JSON'i yazar.</summary>
    private static string ReSerialize(CompanyRating rating, string? fallback)
    {
        try
        {
            return JsonSerializer.Serialize(rating);
        }
        catch
        {
            return fallback ?? "{}";
        }
    }

    private static CompanyAnalysis? ParseAnalysis(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CompanyAnalysis>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
