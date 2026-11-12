using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EgebisLeadFinder.Tests;

[Collection("NewsScan")]
public class NewsLiveTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan TopicEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CompanyEvery = TimeSpan.FromMinutes(60);

    private static ApplicationDbContext Db() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    // ---------------- Sıra ----------------

    [Fact]
    public void Hic_taranmamislar_once_gelir_radar_firmadan_once()
    {
        var picked = NewsQueue.Pick(new[]
        {
            new NewsTarget(NewsCursorKinds.Company, 1, Now.AddHours(-5)),
            new NewsTarget(NewsCursorKinds.Company, 2, null),
            new NewsTarget(NewsCursorKinds.Topic, 9, null),
            new NewsTarget(NewsCursorKinds.Topic, 8, Now.AddHours(-2))
        }, Now, 12, TopicEvery, CompanyEvery);

        Assert.Equal(new[] { (NewsCursorKinds.Topic, 9), (NewsCursorKinds.Company, 2), (NewsCursorKinds.Topic, 8), (NewsCursorKinds.Company, 1) },
            picked.Select(p => (p.Kind, p.RefId)));
    }

    [Fact]
    public void Suresi_dolmayanlar_atlanir_en_eski_firma_once_ve_tur_sinirina_uyulur()
    {
        var targets = new List<NewsTarget>
        {
            new(NewsCursorKinds.Company, 1, Now.AddMinutes(-20)),   // 60 dk dolmadı
            new(NewsCursorKinds.Topic, 5, Now.AddMinutes(-10)),     // 30 dk dolmadı
            new(NewsCursorKinds.Company, 2, Now.AddHours(-3)),
            new(NewsCursorKinds.Company, 3, Now.AddHours(-9))
        };

        var picked = NewsQueue.Pick(targets, Now, 12, TopicEvery, CompanyEvery);
        Assert.Equal(new[] { 3, 2 }, picked.Select(p => p.RefId));

        Assert.Single(NewsQueue.Pick(targets, Now, 1, TopicEvery, CompanyEvery));
        Assert.Empty(NewsQueue.Pick(targets, Now, 0, TopicEvery, CompanyEvery));
    }

    // ---------------- Haber puanı ----------------

    [Fact]
    public void Haber_artisi_sadece_son_30_gunun_yatirim_haberlerinden_gelir_en_fazla_15()
    {
        var items = new List<(string, int, DateTime)>
        {
            (NewsCategories.Investment, 5, Now.AddDays(-1)),   // +10
            (NewsCategories.Investment, 4, Now.AddDays(-3)),   // +6  -> 16, tavan 15
            (NewsCategories.Risk, 5, Now.AddDays(-1)),         // risk puan düşürmez ve artırmaz
            (NewsCategories.Investment, 5, Now.AddDays(-40))   // eski
        };
        Assert.Equal(15, NewsBoostCalculator.Compute(items, Now));

        Assert.Equal(3, NewsBoostCalculator.Compute(new[] { (NewsCategories.Investment, 3, Now.AddDays(-2)) }, Now));
        Assert.Equal(0, NewsBoostCalculator.Compute(new[] { (NewsCategories.Investment, 2, Now.AddDays(-2)) }, Now));
        Assert.Equal(0, NewsBoostCalculator.Compute(new[] { (NewsCategories.Investment, 5, Now.AddDays(-31)) }, Now));
    }

    [Fact]
    public void Puan_dokumunde_haber_sinyali_gorunur_artis_yoksa_kalem_yoktur()
    {
        var scoring = new LeadScoringService(Options.Create(new ScoringOptions()));
        var plain = scoring.ScoreCompany(null, null, null, icp: null, company: new Company { Name = "A" });
        var boosted = scoring.ScoreCompany(null, null, null, icp: null, company: new Company { Name = "A", NewsBoost = 10 });

        Assert.DoesNotContain(plain.Items, i => i.Reason.StartsWith("Haber sinyali"));
        Assert.Contains(boosted.Items, i => i.Reason.StartsWith("Haber sinyali") && i.Points == 10);
    }

    // ---------------- Tarama: olumlu haber → artış ----------------

    private sealed class FixedSource : INewsSource
    {
        private readonly NewsHit[] _hits;
        public FixedSource(params NewsHit[] hits) => _hits = hits;
        public string Name => "ana";
        public bool IsFallback => false;
        public Task<List<NewsHit>> SearchAsync(string query, CancellationToken ct = default) => Task.FromResult(_hits.ToList());
    }

    [Fact]
    public async Task Takip_edilen_firmaya_yeni_fabrika_haberi_gelince_haber_artisi_yazilir()
    {
        using var db = Db();
        var company = new Company { Name = "Vestel Elektronik", Domain = "vestel.com.tr", City = "Manisa", Score = 40 };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        db.CompanyWatches.Add(new CompanyWatch { UserId = 1, CompanyId = company.Id });
        await db.SaveChangesAsync();

        var source = new FixedSource(new NewsHit("Vestel Elektronik Manisa'da yeni fabrika açacak", "https://a.com/1", "A", DateTime.UtcNow, null));
        var service = new NewsScanService(db, new INewsSource[] { source }, new FakeSettingsService(),
            Options.Create(new NewsOptions { RequestDelayMs = 0 }), Options.Create(new ResearchOptions()), NullLogger<NewsScanService>.Instance);

        await service.RunAsync(CancellationToken.None);

        var saved = await db.Companies.SingleAsync();
        Assert.Equal(10, saved.NewsBoost);          // yeni fabrika = önem 5 yatırım haberi = +10
        Assert.NotNull(saved.NewsBoostAt);
    }

    [Fact]
    public void Ajan_durumu_tur_ilerlemesini_ve_sonraki_turu_tasir()
    {
        var status = new NewsAgentStatus();
        status.BeginTick(12);
        status.Progress("Vestel Elektronik", 4);

        var scanning = status.Snapshot();
        Assert.Equal(NewsAgentStatus.Scanning, scanning.State);
        Assert.Equal("Vestel Elektronik", scanning.CurrentQuery);
        Assert.Equal((4, 12), (scanning.Done, scanning.Total));

        var next = Now.AddMinutes(10);
        status.EndTick(Now, next, restUntil: null);
        var idle = status.Snapshot();
        Assert.Equal(NewsAgentStatus.Idle, idle.State);
        Assert.Equal(next, idle.NextTickAt);

        status.EndTick(Now, next, restUntil: Now.AddMinutes(30));
        Assert.Equal(NewsAgentStatus.Resting, status.Snapshot().State);
    }

    [Fact]
    public void Yedek_kaynak_gunluk_tavana_uyar_ve_gun_degisince_sifirlanir()
    {
        var health = new NewsSourceHealth();
        var day1 = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(health.TryUseSerper(day1, 2));
        Assert.True(health.TryUseSerper(day1, 2));
        Assert.False(health.TryUseSerper(day1, 2));
        Assert.True(health.TryUseSerper(day1.AddDays(1), 2));
    }
}
