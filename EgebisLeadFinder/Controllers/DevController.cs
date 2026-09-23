using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>
/// Servisleri UI olmadan tek tek denemek icin gelistirme uclari.
/// Sadece Development ortaminda cevap verir.
/// </summary>
[Route("dev")]
public class DevController : Controller
{
    private readonly ISearchService _search;
    private readonly IWebScraperService _scraper;
    private readonly IAiService _ai;
    private readonly LeadDiscoveryService _discovery;
    private readonly ICompanyResearchService _research;
    private readonly IWebHostEnvironment _env;

    public DevController(
        ISearchService search,
        IWebScraperService scraper,
        IAiService ai,
        LeadDiscoveryService discovery,
        ICompanyResearchService research,
        IWebHostEnvironment env)
    {
        _search = search;
        _scraper = scraper;
        _ai = ai;
        _discovery = discovery;
        _research = research;
        _env = env;
    }

    /// <summary>
    /// LinkedIn arama sonuclarinin ham halini ve ayristirma ciktisini yan yana gosterir.
    /// Unvanin neden eksik veya kirpik geldigini teshis etmek icin.
    /// </summary>
    [HttpGet("linkedin")]
    public async Task<IActionResult> LinkedIn(string company, CancellationToken ct = default)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var profiles = await _search.SearchLinkedInProfilesAsync(company, ct);

        return Json(profiles.Select(p =>
        {
            var parsed = LinkedInTitleParser.Parse(p.Title, p.Url, p.Snippet);
            return new
            {
                rawTitle = p.Title,
                rawSnippet = p.Snippet,
                parsedName = parsed?.Name,
                parsedTitle = parsed?.Title
            };
        }));
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(string industry = "Otomotiv", string? city = "Bursa",
        string country = "Türkiye", int max = 20, CancellationToken ct = default)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var criteria = new SearchCriteria
        {
            Industry = industry,
            City = city,
            Country = country,
            MaxCompanies = max
        };

        var queries = SearchQueryBuilder.Build(criteria);

        try
        {
            var results = await _search.SearchCompaniesAsync(criteria, ct);
            return Json(new { queries, count = results.Count, results });
        }
        catch (Exception ex)
        {
            return Json(new { queries, error = ex.Message });
        }
    }

    [HttpGet("scrape")]
    public async Task<IActionResult> Scrape(string url, CancellationToken ct = default)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(url)) return BadRequest("url parametresi gerekli");

        var site = await _scraper.ScrapeAsync(url, ct);

        return Json(new
        {
            site.Url,
            site.Success,
            site.Error,
            site.VisitedUrls,
            textLength = site.Text.Length,
            site.Emails,
            site.Phones,
            people = site.People.Select(p => new { p.Name, p.Title, p.Email }),
            textPreview = site.Text.Length > 600 ? site.Text[..600] : site.Text
        });
    }

    /// <summary>Scrape + AI analizi tek adimda: bir firmanin SAP kullanip kullanmadigini gorur.</summary>
    [HttpGet("analyze")]
    public async Task<IActionResult> Analyze(string url, CancellationToken ct = default)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(url)) return BadRequest("url parametresi gerekli");

        var site = await _scraper.ScrapeAsync(url, ct);
        if (!site.Success)
            return Json(new { url, scrapeError = site.Error });

        var result = await _ai.AnalyzeCompanyAsync(site.Text, ct);

        return Json(new
        {
            url,
            pages = site.VisitedUrls.Count,
            textLength = site.Text.Length,
            emails = site.Emails,
            aiSuccess = result.Success,
            aiError = result.Error,
            analysis = result.Analysis
        });
    }

    /// <summary>
    /// Faz-II on arastirma: toplanan ham kaynak parcalari + AI ciktisi + deterministik
    /// sinyal yan yana. Kaydetmez; sadece gorur.
    /// </summary>
    [HttpGet("research")]
    public async Task<IActionResult> Research(string company, string? website = null, string? city = null,
        CancellationToken ct = default)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrWhiteSpace(company)) return BadRequest("company parametresi gerekli");

        var stub = new Company { Name = company, Website = website, City = city };
        var result = await _research.ResearchAsync(stub, ct);

        return Json(new
        {
            success = result.Success,
            error = result.Error,
            signal = result.Evaluation?.Signal.ToString(),
            evaluatorNotes = result.Evaluation?.Items.Select(i => new { i.Reason, direction = i.Direction.ToString() }),
            sources = result.Sources.Select(s => new
            {
                s.SourceName,
                s.FoundSomething,
                s.Error,
                snippetCount = s.Snippets.Count,
                snippets = s.Snippets.Select(sn => new { sn.Kind, sn.SourceUrl, sn.Text })
            }),
            links = result.Links.Select(l => new { l.Label, l.Url }),
            rating = result.Rating
        });
    }

    /// <summary>Uctan uca akis: ara, oku, analiz et, puanla, kaydet.</summary>
    [HttpGet("pipeline")]
    public async Task<IActionResult> Pipeline(string industry = "Otomotiv", string? city = "Bursa",
        string country = "Türkiye", int max = 5, CancellationToken ct = default)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var criteria = new SearchCriteria
        {
            Industry = industry,
            City = city,
            Country = country,
            MaxCompanies = max
        };

        var result = await _discovery.RunAsync(criteria, ct);

        return Json(new
        {
            result.FoundBySearch,
            result.AlreadyKnown,
            result.Processed,
            result.Failed,
            companies = result.Companies.Select(c => new
            {
                c.Id,
                c.Name,
                c.Domain,
                c.Industry,
                c.Score,
                c.ProcessingError,
                contacts = c.Contacts.Select(ct2 => new { ct2.Name, ct2.Title, ct2.Email, ct2.TitleScore }),
                analysis = c.AiAnalysis
            })
        });
    }
}
