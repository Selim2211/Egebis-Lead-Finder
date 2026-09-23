using System.Text.RegularExpressions;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services.CompanyIntel;

/// <summary>
/// Haber / itibar / risk / buyume / yonetim / teknoloji sinyallerini Google (Serper)
/// uzerinden toplar. En degerli sonuclarin sayfalari acilip makale metni okunur;
/// sosyal medya kazinmaz.
/// </summary>
public class NewsIntelSource : ICompanyIntelSource
{
    private readonly ISearchService _search;
    private readonly ResearchOptions _options;
    private readonly ILogger<NewsIntelSource> _logger;
    private readonly IWebScraperService? _scraper;

    // Giris duvari / bot korumasi olan ya da makale olmayan siteler: acmak kredi ve sure harcar.
    private static readonly string[] SkipFetchHosts =
    {
        "linkedin.com", "facebook.com", "instagram.com", "twitter.com", "x.com",
        "youtube.com", "kariyer.net", "tiktok.com"
    };

    public NewsIntelSource(
        ISearchService search,
        IOptions<ResearchOptions> options,
        ILogger<NewsIntelSource> logger,
        IWebScraperService? scraper = null)
    {
        _search = search;
        _options = options.Value;
        _logger = logger;
        _scraper = scraper;
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
                        Kind = Classify(text),
                        Date = r.Date
                    });
                }
            }
            catch (Exception ex)
            {
                // Tek sorgu patlarsa digerleri devam etsin.
                _logger.LogWarning(ex, "On araştırma sorgusu başarısız: {Query}", query);
            }
        }

        await EnrichWithArticlesAsync(intel.Snippets, company, ct);

        _logger.LogInformation(
            "{Company}: {Count} haber/web parçası toplandı.", company.Name, intel.Snippets.Count);

        return intel;
    }

    /// <summary>
    /// Snippet'ler 1-2 cumledir; en onemli sonuclarin sayfasi acilip makale govdesi
    /// eklenir. Sayfa okunamazsa snippet oldugu gibi kalir.
    /// </summary>
    private async Task EnrichWithArticlesAsync(List<IntelSnippet> snippets, Company company, CancellationToken ct)
    {
        if (_scraper is null || _options.MaxArticlesToFetch <= 0 || snippets.Count == 0) return;

        var ownHost = HostOf(company.Website);

        var candidates = snippets
            .Select((s, i) => (Snippet: s, Index: i))
            .Where(x => IsFetchable(x.Snippet.SourceUrl, ownHost))
            .OrderBy(x => FetchPriority(x.Snippet.Kind))
            .ThenBy(x => x.Index)
            .Take(_options.MaxArticlesToFetch)
            .ToList();

        using var throttle = new SemaphoreSlim(3);

        await Task.WhenAll(candidates.Select(async c =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var body = await _scraper.FetchPageTextAsync(c.Snippet.SourceUrl!, _options.ArticleMaxChars, ct);
                if (body is null) return;

                snippets[c.Index] = new IntelSnippet
                {
                    Text = $"{c.Snippet.Text}\n{body}",
                    SourceUrl = c.Snippet.SourceUrl,
                    Kind = c.Snippet.Kind,
                    Date = c.Snippet.Date
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Haber sayfası okunamadı: {Url}", c.Snippet.SourceUrl);
            }
            finally
            {
                throttle.Release();
            }
        }));
    }

    private static int FetchPriority(IntelKind kind) => kind switch
    {
        IntelKind.Risk => 0,
        IntelKind.Finansal => 1,
        IntelKind.Buyume => 2,
        IntelKind.Teknoloji => 3,
        IntelKind.Yonetim => 4,
        _ => 5
    };

    private static bool IsFetchable(string? url, string? ownHost)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return false;

        var host = uri.Host.ToLowerInvariant();
        if (ownHost is not null && host.EndsWith(ownHost)) return false; // firma sitesi WebsiteIntelSource'ta okunur
        return !SkipFetchHosts.Any(h => host == h || host.EndsWith("." + h));
    }

    private static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host.ToLowerInvariant().Replace("www.", "")
            : null;

    /// <summary>Snippet metnini kaba bir ture ayirir; deterministik degerlendirme buna bakar.</summary>
    private IntelKind Classify(string text)
    {
        if (ContainsAny(text, _options.RiskKeywords)) return IntelKind.Risk;
        if (ContainsAny(text, _options.FinanceKeywords)) return IntelKind.Finansal;
        if (ContainsAny(text, _options.GrowthKeywords)) return IntelKind.Buyume;
        if (ContainsAny(text, _options.TechnologyKeywords, wholeWordShort: true)) return IntelKind.Teknoloji;
        if (ContainsAny(text, _options.ManagementKeywords)) return IntelKind.Yonetim;
        return IntelKind.Haber;
    }

    /// <summary>
    /// Teknoloji listesindeki kisa kisaltmalar ("sap", "mes", "erp") tam kelime aranir ki
    /// "sapma", "mesaj" eslesmesin. Diger listelerde Turkce ekler ("icraya") icin alt dize yeterli.
    /// </summary>
    private static bool ContainsAny(string text, IEnumerable<string> keywords, bool wholeWordShort = false) =>
        keywords.Any(k => wholeWordShort && k.Length <= 4
            ? Regex.IsMatch(text, $@"(?<!\p{{L}}){Regex.Escape(k)}(?!\p{{L}})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            : text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
