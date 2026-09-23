using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services.CompanyIntel;

/// <summary>
/// Haber / itibar / risk / buyume sinyallerini Google (Serper) uzerinden toplar.
/// Sosyal medya kazinmaz; yalnizca arama sonucu basliklari ve snippet'leri okunur.
/// </summary>
public class NewsIntelSource : ICompanyIntelSource
{
    private readonly ISearchService _search;
    private readonly ResearchOptions _options;
    private readonly ILogger<NewsIntelSource> _logger;

    public NewsIntelSource(
        ISearchService search,
        IOptions<ResearchOptions> options,
        ILogger<NewsIntelSource> logger)
    {
        _search = search;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "Haber ve web araması";

    public async Task<SourceIntel> CollectAsync(Company company, CancellationToken ct = default)
    {
        var intel = new SourceIntel { SourceName = Name };

        var queries = CompanyResearchQueryBuilder.Build(company, _options);
        if (queries.Count == 0)
        {
            intel.Error = "Firma adı yok, arama yapılamadı.";
            return intel;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var query in queries)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var results = await _search.SearchAsync(query, _options.ResultsPerQuery, ct);

                foreach (var r in results)
                {
                    var text = $"{r.Title}. {r.Snippet}".Trim();
                    if (text.Length < 15) continue;

                    // Ayni haber birden fazla sorguda cikabilir.
                    var key = r.Url ?? text;
                    if (!seen.Add(key)) continue;

                    intel.Snippets.Add(new IntelSnippet
                    {
                        Text = text,
                        SourceUrl = r.Url,
                        Kind = Classify(text)
                    });
                }
            }
            catch (Exception ex)
            {
                // Tek sorgu patlarsa digerleri devam etsin.
                _logger.LogWarning(ex, "On araştırma sorgusu başarısız: {Query}", query);
            }
        }

        _logger.LogInformation(
            "{Company}: {Count} haber/web parçası toplandı.", company.Name, intel.Snippets.Count);

        return intel;
    }

    /// <summary>Snippet metnini kaba bir ture ayirir; deterministik degerlendirme buna bakar.</summary>
    private IntelKind Classify(string text)
    {
        if (ContainsAny(text, _options.RiskKeywords)) return IntelKind.Risk;
        if (ContainsAny(text, _options.FinanceKeywords)) return IntelKind.Finansal;
        if (ContainsAny(text, _options.GrowthKeywords)) return IntelKind.Buyume;
        return IntelKind.Haber;
    }

    private static bool ContainsAny(string text, IEnumerable<string> keywords) =>
        keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
