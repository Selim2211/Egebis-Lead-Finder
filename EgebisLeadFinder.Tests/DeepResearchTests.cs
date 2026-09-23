using System.Text.Json;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Derin analiz: firma sitesi okunur, onemli haberlerin sayfasi acilir, AI'a giden
/// metin onem sirasiyla kesilir; eski kayitlar yeni alanlar bos olarak okunur.
/// </summary>
public class DeepResearchTests
{
    [Fact]
    public async Task Site_sayfalari_Site_parcasi_olur()
    {
        var scraper = new FakeScraper
        {
            Pages = { new ScrapedPage("https://ornek.com/hakkimizda", "1985'te kurulduk, 350 çalışan...") }
        };
        var source = new WebsiteIntelSource(scraper, Options.Create(new ResearchOptions()),
            NullLogger<WebsiteIntelSource>.Instance);

        var intel = await source.CollectAsync(new Company { Name = "Örnek", Website = "ornek.com" });

        var snippet = Assert.Single(intel.Snippets);
        Assert.Equal(IntelKind.Site, snippet.Kind);
        Assert.Equal("https://ornek.com", scraper.LastSite);
    }

    [Fact]
    public async Task Web_sitesi_yoksa_kaynak_bos_doner()
    {
        var source = new WebsiteIntelSource(new FakeScraper(), Options.Create(new ResearchOptions()),
            NullLogger<WebsiteIntelSource>.Instance);

        var intel = await source.CollectAsync(new Company { Name = "Örnek" });

        Assert.False(intel.FoundSomething);
        Assert.NotNull(intel.Error);
    }

    [Fact]
    public async Task Haber_sayfasi_acilir_okunamazsa_snippet_kalir_sosyal_medya_acilmaz()
    {
        var search = new FixedSearch(new()
        {
            new() { Title = "Örnek yeni fabrika yatırımı", Url = "https://haber.com/1", Snippet = "kısa", Date = "3 gün önce" },
            new() { Title = "Örnek duyuru", Url = "https://haber.com/2", Snippet = "kısa" },
            new() { Title = "Örnek LinkedIn", Url = "https://www.linkedin.com/company/ornek", Snippet = "kısa" }
        });
        var scraper = new FakeScraper { Articles = { ["https://haber.com/1"] = "Makale gövdesi: 50 milyon TL yatırım." } };

        var source = new NewsIntelSource(search, Options.Create(new ResearchOptions { MaxNewsQueries = 1 }),
            NullLogger<NewsIntelSource>.Instance, scraper);

        var intel = await source.CollectAsync(new Company { Name = "Örnek" });

        var enriched = intel.Snippets.Single(s => s.SourceUrl == "https://haber.com/1");
        Assert.Contains("Makale gövdesi", enriched.Text);
        Assert.Equal("3 gün önce", enriched.Date);
        Assert.DoesNotContain("Makale", intel.Snippets.Single(s => s.SourceUrl == "https://haber.com/2").Text);
        Assert.DoesNotContain(scraper.Fetched, u => u.Contains("linkedin"));
    }

    [Theory]
    [InlineData("Örnek A.Ş. SAP'ye geçti", IntelKind.Teknoloji)]
    [InlineData("Örnek A.Ş. yönünde sapma yaşandı", IntelKind.Haber)]
    public async Task Kisa_teknoloji_kelimeleri_tam_kelime_aranir(string title, IntelKind expected)
    {
        var search = new FixedSearch(new() { new() { Title = title, Url = "https://x.com/1", Snippet = "" } });
        var source = new NewsIntelSource(search, Options.Create(new ResearchOptions { MaxNewsQueries = 1 }),
            NullLogger<NewsIntelSource>.Instance);

        var intel = await source.CollectAsync(new Company { Name = "Örnek" });

        Assert.Equal(expected, Assert.Single(intel.Snippets).Kind);
    }

    [Fact]
    public void AI_metni_onem_sirasiyla_kesilir_parca_bolunmez()
    {
        var snippets = new List<IntelSnippet>
        {
            new() { Text = new string('h', 300), Kind = IntelKind.Haber, SourceUrl = "https://h" },
            new() { Text = "konkordato", Kind = IntelKind.Risk, SourceUrl = "https://r" },
            new() { Text = "hakkımızda", Kind = IntelKind.Site, SourceUrl = "https://s" }
        };

        var block = GeminiAiService.BuildSourcesBlock(snippets, 200);

        Assert.StartsWith("[1] (Risk)", block);
        Assert.Contains("(Site)", block);
        Assert.DoesNotContain("hhh", block);
    }

    [Fact]
    public void Eski_rating_kaydi_yeni_alanlar_bos_okunur()
    {
        const string old = """{"signal":"guclu","summary":"Eski özet","financialSource":"yok","customers":["A"]}""";

        var rating = JsonSerializer.Deserialize<CompanyRating>(old)!;

        Assert.Equal("Eski özet", rating.Summary);
        Assert.Empty(rating.Management);
        Assert.Empty(rating.FinancialPeriods);
        Assert.Empty(rating.NewsTimeline);
        Assert.Null(rating.Technology);
    }

    [Fact]
    public void Kaynakli_donem_cirosu_finansal_kanit_sayilir()
    {
        var rating = new CompanyRating
        {
            FinancialPeriods = { new AiFinancialPeriod { Period = "2025", Revenue = "1,2 milyar TL", SourceUrl = "https://haber/1" } },
            GrowthSignals = { "Yeni fabrika" }
        };

        var evaluation = new CompanyRatingEvaluator(Options.Create(new ResearchOptions()))
            .Evaluate(rating, new List<IntelSnippet> { new() { Text = "yatırım", Kind = IntelKind.Buyume } });

        Assert.Equal(RatingSignal.Guclu, evaluation.Signal);
    }

    private class FakeScraper : IWebScraperService
    {
        public List<ScrapedPage> Pages { get; } = new();
        public Dictionary<string, string> Articles { get; } = new();
        public List<string> Fetched { get; } = new();
        public string? LastSite { get; private set; }

        public Task<ScrapedSite> ScrapeAsync(string siteUrl, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<List<ScrapedPage>> ReadPagesAsync(string siteUrl, IEnumerable<string> paths, int maxPages,
            int maxCharsPerPage, CancellationToken ct = default)
        {
            LastSite = siteUrl;
            return Task.FromResult(Pages);
        }

        public Task<string?> FetchPageTextAsync(string url, int maxChars, CancellationToken ct = default)
        {
            lock (Fetched) Fetched.Add(url);
            return Task.FromResult(Articles.TryGetValue(url, out var body) ? body : null);
        }
    }

    private class FixedSearch : ISearchService
    {
        private readonly List<SearchResult> _results;
        public FixedSearch(List<SearchResult> results) => _results = results;

        public Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default) =>
            Task.FromResult(_results);

        public Task<List<SearchResult>> SearchCompaniesAsync(SearchCriteria criteria, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<List<SearchResult>> SearchLinkedInProfilesAsync(string companyName, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
