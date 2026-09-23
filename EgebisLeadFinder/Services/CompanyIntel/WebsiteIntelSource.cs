using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services.CompanyIntel;

/// <summary>
/// Firmanin kendi sitesini okur: hakkimizda, urunler, referanslar, kariyer, haberler,
/// yatirimci iliskileri... Buyukluk, yonetim, musteri ve teknoloji bilgisinin
/// cogu Google snippet'lerinde degil bu sayfalarda yazar.
/// </summary>
public class WebsiteIntelSource : ICompanyIntelSource
{
    private readonly IWebScraperService _scraper;
    private readonly ResearchOptions _options;
    private readonly ILogger<WebsiteIntelSource> _logger;

    public WebsiteIntelSource(
        IWebScraperService scraper,
        IOptions<ResearchOptions> options,
        ILogger<WebsiteIntelSource> logger)
    {
        _scraper = scraper;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "Firma web sitesi";

    public async Task<SourceIntel> CollectAsync(Company company, CancellationToken ct = default)
    {
        var intel = new SourceIntel { SourceName = Name };

        if (string.IsNullOrWhiteSpace(company.Website))
        {
            intel.Error = "Firmanın web sitesi kayıtlı değil.";
            return intel;
        }

        var site = company.Website.Trim();
        if (!site.StartsWith("http", StringComparison.OrdinalIgnoreCase)) site = "https://" + site;

        var pages = await _scraper.ReadPagesAsync(
            site, _options.WebsitePaths, _options.MaxWebsitePages, _options.WebsitePageMaxChars, ct);

        foreach (var page in pages)
        {
            intel.Snippets.Add(new IntelSnippet
            {
                Text = page.Text,
                SourceUrl = page.Url,
                Kind = IntelKind.Site
            });
        }

        if (pages.Count == 0) intel.Error = "Sitede okunabilir sayfa bulunamadı.";

        _logger.LogInformation("{Company}: sitede {Count} sayfa okundu.", company.Name, pages.Count);
        return intel;
    }
}
