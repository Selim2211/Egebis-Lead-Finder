using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EgebisLeadFinder.Tests;

// Tarama servisi statik kilit kullanır; bu sınıflar birbiriyle paralel koşmamalı.
[Collection("NewsScan")]
public class NewsFeedTests
{
    private const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0"><channel>
          <item>
            <title>Vestel Manisa'da yeni fabrika yatırımı yapacak - Dünya Gazetesi</title>
            <link>https://news.google.com/rss/articles/abc123</link>
            <pubDate>Tue, 10 Nov 2026 08:30:00 GMT</pubDate>
            <description>&lt;a href="x"&gt;Vestel&lt;/a&gt; yeni tesis için &lt;b&gt;temel atıyor&lt;/b&gt;.</description>
            <source url="https://www.dunya.com">Dünya Gazetesi</source>
          </item>
          <item><title></title><link>https://x.com/a</link></item>
        </channel></rss>
        """;

    private static ApplicationDbContext Db() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeSource : INewsSource
    {
        private readonly Func<string, List<NewsHit>> _fn;
        public FakeSource(bool fallback, Func<string, List<NewsHit>> fn) { IsFallback = fallback; _fn = fn; }
        public string Name => IsFallback ? "yedek" : "ana";
        public bool IsFallback { get; }
        public int Calls { get; private set; }
        public Task<List<NewsHit>> SearchAsync(string query, CancellationToken ct = default) { Calls++; return Task.FromResult(_fn(query)); }
    }

    private static NewsScanService Service(ApplicationDbContext db, IEnumerable<INewsSource> sources, bool fallback = true,
        NewsSourceHealth? health = null, int queriesPerTick = 12) =>
        new(db, sources, new FakeSettingsService(),
            Options.Create(new NewsOptions { RequestDelayMs = 0, SerperFallback = fallback, QueriesPerTick = queriesPerTick }),
            Options.Create(new ResearchOptions()), NullLogger<NewsScanService>.Instance, health: health);

    [Fact]
    public void Rss_baslik_kaynak_tarih_ve_ozet_ayrilir_bos_baslik_atlanir()
    {
        var hits = GoogleNewsRssSource.ParseRss(Rss);

        var hit = Assert.Single(hits);
        Assert.Equal("Vestel Manisa'da yeni fabrika yatırımı yapacak", hit.Title);
        Assert.Equal("Dünya Gazetesi", hit.Source);
        Assert.Equal(new DateTime(2026, 11, 10, 8, 30, 0, DateTimeKind.Utc), hit.PublishedUtc);
        Assert.DoesNotContain("<", hit.Snippet);
    }

    [Fact]
    public void Bozuk_rss_bos_liste_doner()
    {
        Assert.Empty(GoogleNewsRssSource.ParseRss("<html>not xml"));
    }

    [Fact]
    public void Url_ozeti_izleme_parametresini_ve_www_yi_yok_sayar()
    {
        var a = NewsDedup.UrlHash("https://www.Site.com/haber/1/?utm_source=x&id=5");
        var b = NewsDedup.UrlHash("https://site.com/haber/1?id=5");

        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.NotEqual(a, NewsDedup.UrlHash("https://site.com/haber/2?id=5"));
    }

    [Fact]
    public void Baslik_anahtari_turkce_harf_ve_noktalama_farkini_siler()
    {
        Assert.Equal(NewsDedup.TitleKey("Vestel: Yeni Fabrika!"), NewsDedup.TitleKey("vestel yeni fabrika"));
    }

    [Theory]
    [InlineData("Firma konkordato ilan etti", NewsCategories.Risk, 4)]
    [InlineData("Şirket yeni fabrika açacak", NewsCategories.Investment, 5)]
    [InlineData("İhracat artışıyla büyüme sürüyor", NewsCategories.Investment, 3)]
    [InlineData("Net kâr açıklandı", NewsCategories.Finance, 2)]
    [InlineData("Bugün hava güzel", NewsCategories.General, 1)]
    public void Kural_siniflama_kategori_ve_onem_verir(string text, string category, int importance)
    {
        var (c, i) = NewsRuleClassifier.Classify(text, new ResearchOptions());

        Assert.Equal(category, c);
        Assert.Equal(importance, i);
    }

    [Fact]
    public void Turkiye_gununun_baslangici_gece_yarisi_utc_arti_3tur()
    {
        // 10 Kasim 2026 01:30 UTC = 04:30 Turkiye -> gun 10 Kasim 00:00 TR = 9 Kasim 21:00 UTC
        Assert.Equal(new DateTime(2026, 11, 9, 21, 0, 0, DateTimeKind.Utc), NewsSlots.TodayStartUtc(new DateTime(2026, 11, 10, 1, 30, 0, DateTimeKind.Utc)));
        // 10 Kasim 2026 22:00 UTC = 11 Kasim 01:00 Turkiye -> gun 11 Kasim
        Assert.Equal(new DateTime(2026, 11, 10, 21, 0, 0, DateTimeKind.Utc), NewsSlots.TodayStartUtc(new DateTime(2026, 11, 10, 22, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Tarama_takip_ve_radar_haberini_kaydeder_baska_firmayi_ve_tekrari_eler()
    {
        using var db = Db();
        var company = new Company { Name = "Vestel Elektronik", Domain = "vestel.com.tr", City = "Manisa" };
        db.Companies.Add(company);
        db.RadarTopics.Add(new RadarTopic { Query = "yeni fabrika yatırımı", City = "Manisa" });
        await db.SaveChangesAsync();
        db.CompanyWatches.Add(new CompanyWatch { UserId = 1, CompanyId = company.Id });
        await db.SaveChangesAsync();

        var source = new FakeSource(false, q => q.Contains("Vestel")
            ? new List<NewsHit>
            {
                new("Vestel Elektronik yeni fabrika açacak", "https://a.com/1", "A", DateTime.UtcNow, null),
                new("Başka Firma Sanayi konkordato ilan etti", "https://a.com/2", "A", DateTime.UtcNow, null)
            }
            : new List<NewsHit> { new("Manisa'da dev yatırım: üretim tesisi kuruluyor", "https://b.com/9", "B", DateTime.UtcNow, null) });

        var run = await Service(db, new[] { source }).RunAsync(CancellationToken.None);

        Assert.NotNull(run);
        Assert.Null(run!.Error);
        Assert.Equal(2, run.Queries);
        Assert.Equal(2, run.NewCount);
        var watch = await db.NewsItems.SingleAsync(n => n.Origin == NewsOrigins.Watch);
        Assert.Equal(company.Id, watch.CompanyId);
        Assert.Equal(5, watch.Importance);
        Assert.Equal(NewsOrigins.Radar, (await db.NewsItems.SingleAsync(n => n.Origin == NewsOrigins.Radar)).Origin);

        var second = await Service(db, new[] { source }).RunAsync(CancellationToken.None);
        Assert.Equal(0, second!.NewCount); // ayni haberler tekrar yazilmaz
        Assert.Equal(2, await db.NewsItems.CountAsync());
    }

    private static async Task AddTopicsAsync(ApplicationDbContext db, int count)
    {
        for (var i = 1; i <= count; i++) db.RadarTopics.Add(new RadarTopic { Query = $"yeni fabrika yatırımı {i}" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Ana_kaynak_uc_hata_verince_tur_kesilir_rss_dinlenir_ve_yedege_dusulmez()
    {
        using var db = Db();
        await AddTopicsAsync(db, 6);
        var health = new NewsSourceHealth();
        var backup = new FakeSource(true, _ => new List<NewsHit> { new("Yedekten gelen bir haber başlığı", "https://c.com/1", "C", null, null) });

        var run = await Service(db, new INewsSource[] { new BrokenSource(), backup }, health: health).RunAsync(CancellationToken.None);

        Assert.Equal(3, run!.Queries);              // 3. hatada tur kesildi
        Assert.Equal(0, backup.Calls);              // dinlenmeden önce yedeğe düşülmez
        Assert.Equal(0, run.SerperCredits);
        Assert.Contains("dinlen", run.Error);
        Assert.True(health.IsResting(DateTime.UtcNow));

        var next = await Service(db, new INewsSource[] { new BrokenSource(), backup }, health: health).RunAsync(CancellationToken.None);
        Assert.Equal(0, next!.Queries);             // dinlenirken hiç sorgu atılmaz
        Assert.Contains("dinleniyor", next.Error);
    }

    [Fact]
    public async Task Dinlenme_bittikten_sonra_hata_surerse_yedek_kullanilir_kapaliysa_kullanilmaz()
    {
        var health = new NewsSourceHealth();
        for (var i = 0; i < NewsSourceHealth.FailuresBeforeRest; i++) health.RecordFailure(DateTime.UtcNow.AddHours(-3)); // dinlenme çoktan bitti

        using var db = Db();
        await AddTopicsAsync(db, 1);
        var backup = new FakeSource(true, _ => new List<NewsHit> { new("Ege'de dev fabrika yatırımı duyuruldu", "https://c.com/1", "C", null, null) });
        var run = await Service(db, new INewsSource[] { new BrokenSource(), backup }, health: health).RunAsync(CancellationToken.None);
        Assert.Equal(1, run!.SerperCredits);
        Assert.Equal(1, run.NewCount);

        var health2 = new NewsSourceHealth();
        for (var i = 0; i < NewsSourceHealth.FailuresBeforeRest; i++) health2.RecordFailure(DateTime.UtcNow.AddHours(-3));
        using var db2 = Db();
        await AddTopicsAsync(db2, 1);
        var backup2 = new FakeSource(true, _ => new List<NewsHit> { new("Başka bir haber başlığı burada", "https://c.com/2", "C", null, null) });
        var off = await Service(db2, new INewsSource[] { new BrokenSource(), backup2 }, fallback: false, health: health2).RunAsync(CancellationToken.None);
        Assert.Equal(0, off!.SerperCredits);
        Assert.Equal(0, backup2.Calls);
    }

    [Fact]
    public async Task Donusumlu_tur_12_sorguyla_sinirlidir_kalanlar_sonraki_turda_taranir_dolmayanlar_atlanir()
    {
        using var db = Db();
        await AddTopicsAsync(db, 15);
        var source = new FakeSource(false, _ => new List<NewsHit>());

        var first = await Service(db, new[] { source }).RunAsync(CancellationToken.None);
        Assert.Equal(12, first!.Queries);

        var second = await Service(db, new[] { source }).RunAsync(CancellationToken.None);
        Assert.Equal(3, second!.Queries);           // kalan 3 konu; ilk 12'nin süresi (30 dk) dolmadı

        var third = await Service(db, new[] { source }).RunAsync(CancellationToken.None);
        Assert.Equal(0, third!.Queries);            // hepsi yeni tarandı
        Assert.Equal(15, await db.NewsScanCursors.CountAsync());
    }

    private sealed class BrokenSource : INewsSource
    {
        public string Name => "bozuk";
        public bool IsFallback => false;
        public Task<List<NewsHit>> SearchAsync(string query, CancellationToken ct = default) => throw new HttpRequestException("429");
    }
}
