using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>2. asama: akilli arama terimleri ve siteler okunmadan on eleme.</summary>
public class SmartSearchTests
{
    private sealed class FakePlannerAi : ISearchPlannerAi
    {
        public List<string> Terms { get; set; } = new() { "Kunststoffspritzguss", "Automobilzulieferer" };
        public Func<CandidateScreenRequest, Dictionary<int, CandidateVerdict>>? Screen { get; set; }
        public bool ThrowOnTerms { get; set; }
        public bool ThrowOnScreen { get; set; }
        public int TermCalls { get; private set; }
        public int ScreenCalls { get; private set; }
        public List<CandidateScreenRequest> ScreenRequests { get; } = new();

        public Task<List<string>> SuggestSearchTermsAsync(SearchTermRequest request, CancellationToken ct = default)
        {
            TermCalls++;
            if (ThrowOnTerms) throw new QuotaExceededException("Gemini");
            return Task.FromResult(Terms.ToList());
        }

        public Task<Dictionary<int, CandidateVerdict>> ScreenCandidatesAsync(CandidateScreenRequest request, CancellationToken ct = default)
        {
            ScreenCalls++;
            ScreenRequests.Add(request);
            if (ThrowOnScreen) throw new InvalidOperationException("Gemini hata");
            return Task.FromResult(Screen?.Invoke(request) ?? new Dictionary<int, CandidateVerdict>());
        }
    }

    private static FakeSettingsService Settings(bool withKey = true, BusinessProfile? profile = null)
    {
        var values = new Dictionary<string, string?>();
        if (withKey) values[SettingKeys.GeminiApiKey] = "test-key";
        if (profile is not null) values[SettingKeys.BusinessProfile] = System.Text.Json.JsonSerializer.Serialize(profile);
        return new FakeSettingsService(values);
    }

    private static IcpService Icp(ISettingsService settings, ApplicationDbContext? db = null) =>
        new(settings, new LeadScoringService(Options.Create(new ScoringOptions())),
            db ?? new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
            null!);

    private static SearchPlanService Planner(ISettingsService settings, ISearchPlannerAi ai, IcpService? icp = null) =>
        new(settings, ai, icp ?? Icp(settings), new MemoryCache(new MemoryCacheOptions()), NullLogger<SearchPlanService>.Instance);

    private static SearchResult R(string domain, string title, string? category = null, string? snippet = null) =>
        new() { Domain = domain, Url = "https://" + domain, Title = title, Category = category, Snippet = snippet };

    [Fact]
    public void Sorgular_her_kalipta_tum_terimlerle_kurulur()
    {
        var c = new SearchCriteria { Industry = "Otomotiv", RegionKey = "DE", Country = "Deutschland", SearchTerms = { "Automobilzulieferer", "otomotiv" } };

        var queries = SearchQueryBuilder.BuildPlaceQueries(c);

        Assert.Equal("Otomotiv Hersteller Deutschland", queries[0]);
        Assert.Equal("Automobilzulieferer Hersteller Deutschland", queries[1]);
        Assert.DoesNotContain(queries, q => q.StartsWith("otomotiv ", StringComparison.Ordinal)); // ayni terim tekrar edilmez
        Assert.Equal(queries.Count, queries.Distinct().Count());
        Assert.Equal(2 * SearchRegions.Get("DE").Pack.Places.Length, queries.Count);
    }

    [Fact]
    public void Terimler_birlestirilir_tekrar_ve_ayni_anlam_atilir()
    {
        var segment = new TargetSegment { Name = "Yan sanayi", SearchTerm = "Otomotiv yan sanayi" };

        var terms = SearchPlanService.MergeTerms("Otomotiv", segment,
            new[] { "OTOMOTİV", "\"metal pres\"", "otomotiv yan sanayi", "plastik enjeksiyon", "kalıp", "x" });

        Assert.Equal(new[] { "Otomotiv", "Otomotiv yan sanayi", "metal pres", "plastik enjeksiyon" }, terms);
        Assert.Equal(SearchPlanService.MaxTerms, terms.Count);
    }

    [Theory]
    [InlineData(20, 30)]
    [InlineData(4, 9)]
    [InlineData(100, 150)]
    [InlineData(200, 250)]
    public void Toplama_hedefi_on_eleme_payi_icerir(int max, int expected) =>
        Assert.Equal(expected, SearchPlanService.CollectTargetFor(max));

    [Fact]
    public void Kural_elemesi_baslik_ve_kategoriye_bakar_ozete_bakmaz()
    {
        var excludes = new[] { "bayi", "servis" };

        Assert.Equal("bayi", SearchPlanService.RuleHit(R("a.com", "Ford Yetkili Bayi"), excludes));
        Assert.Equal("servis", SearchPlanService.RuleHit(R("b.com", "Yılmaz Oto", category: "Oto servis"), excludes));
        Assert.Null(SearchPlanService.RuleHit(R("c.com", "Akme Döküm", snippet: "Bayilerimiz tüm Türkiye'de"), excludes));
        Assert.Null(SearchPlanService.RuleHit(R("d.com", "Servisçi Makina"), excludes)); // kelime siniri
        Assert.Equal("sample doku", SearchPlanService.RuleHit(R("e.com", "Sample-Doku"), new[] { "sample doku" }));
    }

    [Fact]
    public void Yapay_zeka_eleme_yaniti_cozulur()
    {
        var verdicts = GeminiAiService.ParseScreenVerdicts("""
            [{"id":1,"keep":true},{"id":2,"keep":false,"reason":"haber sitesi"},{"id":"x"},{"id":3}]
            """);

        Assert.True(verdicts[1].Keep);
        Assert.False(verdicts[2].Keep);
        Assert.Equal("haber sitesi", verdicts[2].Reason);
        Assert.True(verdicts[3].Keep);
        Assert.Equal(3, verdicts.Count);
    }

    [Fact]
    public async Task Anahtar_yoksa_akilli_arama_devre_disi_not_dusulur()
    {
        var ai = new FakePlannerAi();
        var criteria = new SearchCriteria { Industry = "Otomotiv", MaxCompanies = 20 };

        var plan = await Planner(Settings(withKey: false), ai).PrepareAsync(criteria);

        Assert.False(plan.Smart);
        Assert.NotNull(plan.Note);
        Assert.Equal(new[] { "Otomotiv" }, plan.Terms);
        Assert.Empty(criteria.SearchTerms);
        Assert.Equal(20, criteria.EffectiveCollectTarget);
        Assert.Equal(0, ai.TermCalls);
    }

    [Fact]
    public async Task Akilli_arama_terimleri_ve_segmenti_hazirlar_onbellege_alir()
    {
        var profile = new BusinessProfile
        {
            Offering = "Robot", IdealCustomer = "Fabrika",
            Segments = { new TargetSegment { Id = "s1", Name = "Plastik", SearchTerm = "Plastik enjeksiyon", ExcludeKeywords = { "hurda" } } }
        };
        var ai = new FakePlannerAi();
        var planner = Planner(Settings(profile: profile), ai);

        var criteria = new SearchCriteria { Industry = "Plastik", RegionKey = "DE", SegmentId = "s1", MaxCompanies = 20 };
        var plan = await planner.PrepareAsync(criteria);
        await planner.PrepareAsync(new SearchCriteria { Industry = "plastik", RegionKey = "DE", SegmentId = "s1" });

        Assert.True(plan.Smart);
        Assert.Equal("Plastik", plan.Segment!.Name);
        Assert.Equal(new[] { "Plastik", "Plastik enjeksiyon", "Kunststoffspritzguss", "Automobilzulieferer" }, plan.Terms);
        Assert.Equal(plan.Terms.Skip(1), criteria.SearchTerms);
        Assert.Equal(30, criteria.EffectiveCollectTarget);
        Assert.Equal(1, ai.TermCalls); // ayni sektor/bolge/segment onbellekten
    }

    [Fact]
    public async Task Terim_onerisi_basarisizsa_yazilan_sektorle_devam_edilir()
    {
        var criteria = new SearchCriteria { Industry = "Metal" };
        var plan = await Planner(Settings(), new FakePlannerAi { ThrowOnTerms = true }).PrepareAsync(criteria);

        Assert.True(plan.Smart);
        Assert.Equal(new[] { "Metal" }, plan.Terms);
        Assert.Empty(criteria.SearchTerms);
        Assert.Contains("üretilemedi", plan.Note);
    }

    [Fact]
    public async Task Firma_adi_aramasinda_ve_kapaliyken_yapay_zeka_kullanilmaz()
    {
        var ai = new FakePlannerAi();
        var planner = Planner(Settings(), ai);

        var byName = await planner.PrepareAsync(new SearchCriteria { CompanyName = "Toyota" });
        var off = await planner.PrepareAsync(new SearchCriteria { Industry = "Metal", SmartSearch = false });

        Assert.False(byName.Smart);
        Assert.False(off.Smart);
        Assert.Equal(0, ai.TermCalls);
    }

    [Fact]
    public async Task On_eleme_kural_ve_yapay_zeka_ile_calisir()
    {
        var settings = Settings();
        var icp = Icp(settings);
        await icp.SaveAsync(new IcpProfile { ExcludeKeywords = { "bayi" } });
        var ai = new FakePlannerAi
        {
            // 2 numarali aday (kural elemesinden sonra: haber.com) elenir.
            Screen = r => new Dictionary<int, CandidateVerdict> { [1] = new(true, null), [2] = new(false, "haber sitesi") }
        };
        var planner = Planner(settings, ai, icp);
        var plan = new SearchPlan { Smart = true, Terms = { "Metal" } };

        var screen = await planner.ScreenAsync(new List<SearchResult>
        {
            R("dokum.com", "Akme Döküm", "Döküm fabrikası"),
            R("bayi.com", "Renault Bayi"),
            R("haber.com", "Sanayi Haberleri")
        }, new SearchCriteria { Industry = "Metal" }, plan);

        Assert.Equal(new[] { "dokum.com" }, screen.Kept.Select(k => k.Domain));
        Assert.Contains(screen.Dropped, d => d.Domain == "bayi.com" && d.Stage == SearchPlanService.StageRule);
        Assert.Contains(screen.Dropped, d => d.Domain == "haber.com" && d.Reason == "haber sitesi" && d.Stage == SearchPlanService.StageAi);
        Assert.Equal(2, Assert.Single(ai.ScreenRequests).Candidates.Count);
        Assert.Contains("Kategori: Döküm fabrikası", ai.ScreenRequests[0].Candidates[0].Text);
    }

    [Fact]
    public async Task On_eleme_basarisizsa_kimse_elenmez_uyari_verilir()
    {
        var settings = Settings();
        var planner = Planner(settings, new FakePlannerAi { ThrowOnScreen = true });
        var candidates = Enumerable.Range(1, SearchPlanService.ScreenBatchSize + 5).Select(i => R($"f{i}.com", $"Firma {i}")).ToList();

        var screen = await planner.ScreenAsync(candidates, new SearchCriteria { Industry = "Metal" }, new SearchPlan { Smart = true });

        Assert.Equal(candidates.Count, screen.Kept.Count);
        Assert.Empty(screen.Dropped);
        Assert.NotNull(screen.Warning);
    }

    // ---------- Uctan uca: LeadDiscoveryService + MockSearchService ----------

    private sealed class FakeScraper : IWebScraperService
    {
        public List<string> Scraped { get; } = new();

        public Task<ScrapedSite> ScrapeAsync(string siteUrl, CancellationToken ct = default)
        {
            lock (Scraped) Scraped.Add(siteUrl);
            return Task.FromResult(new ScrapedSite { Url = siteUrl, Text = "Üretim yapan firma." });
        }

        public Task<List<ScrapedPage>> ReadPagesAsync(string siteUrl, IEnumerable<string> paths, int maxPages, int maxCharsPerPage, CancellationToken ct = default) =>
            Task.FromResult(new List<ScrapedPage>());

        public Task<string?> FetchPageTextAsync(string url, int maxChars, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoAi : IAiService
    {
        public Task<AiAnalysisResult> AnalyzeCompanyAsync(string siteText, CancellationToken ct = default) =>
            Task.FromResult(AiAnalysisResult.Failed("test"));
    }

    [Fact]
    public async Task Arama_on_elemede_elenen_siteleri_okumaz_ve_limiti_korur()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        await using var provider = services.BuildServiceProvider();

        // Mock aramanin ilk sonucu daha once kayitli.
        await using (var seed = provider.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Companies.Add(new Company { Name = "Eski", Domain = "example-otomotiv.com.tr" });
            await db.SaveChangesAsync();
        }

        var settings = Settings();
        await using var scope = provider.CreateAsyncScope();
        var icp = Icp(settings, scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var ai = new FakePlannerAi
        {
            // Yeni adaylardan 2. ve 3. elenir.
            Screen = r => new Dictionary<int, CandidateVerdict> { [2] = new(false, "firma rehberi"), [3] = new(false, "bayi") }
        };
        var scraper = new FakeScraper();
        var discovery = new LeadDiscoveryService(new MockSearchService(), scraper, new NoAi(),
            new LeadScoringService(Options.Create(new ScoringOptions())), provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PipelineOptions()), NullLogger<LeadDiscoveryService>.Instance, icp, Planner(settings, ai, icp));

        var result = await discovery.RunAsync(new SearchCriteria { Industry = "Otomotiv", MaxCompanies = 6 });

        Assert.Equal(new[] { "Otomotiv", "Kunststoffspritzguss", "Automobilzulieferer" }, result.SearchTerms);
        Assert.Equal(2, result.PreFiltered.Count);
        Assert.Equal(1, result.AlreadyKnown);
        // Limit 6, ilk 6 sonucta 1 kayitli: en fazla 5 yeni firma islenir; elenenlerin yerine yedek adaylar gelir.
        Assert.Equal(5, result.Processed);
        Assert.Equal(5, scraper.Scraped.Count);
        Assert.DoesNotContain(scraper.Scraped, u => u.Contains("demirdokum-ornek.com") || u.Contains("sample-metal.com.tr"));
        Assert.Equal(1 + 2 + 5, result.FoundBySearch);
        Assert.Equal(2, result.OverLimit); // mock 10 aday dondurdu: 1 kayitli + 2 elenen + 5 islenen + 2 fazla
    }
}
