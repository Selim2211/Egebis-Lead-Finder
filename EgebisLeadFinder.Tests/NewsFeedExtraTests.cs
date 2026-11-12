using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Inbox;
using EgebisLeadFinder.Services.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EgebisLeadFinder.Tests;

[Collection("NewsScan")]
public class NewsFeedExtraTests
{
    private static ApplicationDbContext Db() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class OneHit : INewsSource
    {
        private readonly NewsHit[] _hits;
        public OneHit(params NewsHit[] hits) => _hits = hits;
        public string Name => "ana";
        public bool IsFallback => false;
        public Task<List<NewsHit>> SearchAsync(string query, CancellationToken ct = default) => Task.FromResult(_hits.ToList());
    }

    private sealed class FakeAi : INewsAi
    {
        public int Calls { get; private set; }
        public Task<List<NewsAiResult>> EnrichNewsAsync(IReadOnlyList<NewsAiInput> items, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(items.Select(i => new NewsAiResult(i.Index, "Özet cümlesi", "yatirim", 5, "Manisa", "Örnek Makina")).ToList());
        }
    }

    private static NewsScanService Service(ApplicationDbContext db, INewsSource source, INewsAi? ai, NotificationService? notifications, int maxAiCalls = 20) =>
        new(db, new[] { source }, new FakeSettingsService(),
            Options.Create(new NewsOptions { RequestDelayMs = 0, MaxAiCallsPerDay = maxAiCalls }),
            Options.Create(new ResearchOptions()), NullLogger<NewsScanService>.Instance, ai, notifications);

    [Fact]
    public async Task Yapay_zeka_ozet_kategori_onem_ve_radar_firma_adini_doldurur_bildirim_uretir()
    {
        using var db = Db();
        db.Users.Add(new AppUser { Id = 1, UserName = "ali", PasswordHash = "x", IsActive = true });
        db.RadarTopics.Add(new RadarTopic { Query = "yeni fabrika yatırımı", City = "Manisa" });
        await db.SaveChangesAsync();

        var ai = new FakeAi();
        var notifications = new NotificationService(db, new MemoryCache(new MemoryCacheOptions()));
        var run = await Service(db, new OneHit(new NewsHit("Manisa'da dev yatırım haberi geldi", "https://x.com/1", "X", DateTime.UtcNow, null)), ai, notifications)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, run!.AiCalls);
        var item = await db.NewsItems.SingleAsync();
        Assert.Equal("Özet cümlesi", item.Summary);
        Assert.Equal(NewsCategories.Investment, item.Category);
        Assert.Equal(5, item.Importance);
        Assert.Equal("Örnek Makina", item.MentionedCompany);
        // Önem 5 radar haberi tüm aktif kullanıcılara bildirim olur.
        var n = await db.Notifications.SingleAsync();
        Assert.Equal(NotificationKinds.News, n.Kind);
        Assert.Equal(1, n.UserId);
    }

    [Fact]
    public async Task Gunluk_yapay_zeka_tavani_dolunca_cagri_yapilmaz_haber_kural_puaniyla_kalir()
    {
        using var db = Db();
        db.RadarTopics.Add(new RadarTopic { Query = "yeni fabrika yatırımı" });
        db.NewsScanRuns.Add(new NewsScanRun { StartedAt = DateTime.UtcNow, AiCalls = 20 });
        await db.SaveChangesAsync();

        var ai = new FakeAi();
        var run = await Service(db, new OneHit(new NewsHit("Şirket yeni fabrika açacak haberi", "https://x.com/2", "X", DateTime.UtcNow, null)), ai, null, maxAiCalls: 20)
            .RunAsync(CancellationToken.None);

        Assert.Equal(0, ai.Calls);
        Assert.Equal(0, run!.AiCalls);
        Assert.Equal(5, (await db.NewsItems.SingleAsync()).Importance); // kural tabanlı
    }

    [Fact]
    public async Task Takip_kumesi_takip_ve_favorileri_tekrarsiz_birlestirir()
    {
        using var db = Db();
        var a = new Company { Name = "A Makina", Domain = "a.com" };
        var b = new Company { Name = "B Makina", Domain = "b.com" };
        var c = new Company { Name = "C Makina", Domain = "c.com" };
        db.Companies.AddRange(a, b, c);
        await db.SaveChangesAsync();
        db.CompanyWatches.Add(new CompanyWatch { UserId = 1, CompanyId = a.Id });
        db.FavoriteCompanies.AddRange(new FavoriteCompany { UserId = 1, CompanyId = a.Id }, new FavoriteCompany { UserId = 2, CompanyId = b.Id });
        await db.SaveChangesAsync();

        var watched = await Service(db, new OneHit(), null, null).WatchedCompaniesAsync(CancellationToken.None);

        Assert.Equal(new[] { a.Id, b.Id }, watched.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public void Sorgular_firma_adini_tirnaklar_radar_sehri_ekler()
    {
        // Firma adındaki tırnaklar atılır, ad tırnak içine alınır.
        Assert.Equal("\"Vestel Elektronik\" when:3d",
            NewsScanService.CompanyQuery(new Company { Name = "Vestel \"Elektronik\"" }, "3d"));
        Assert.Equal("yeni fabrika yatırımı Manisa when:3d",
            NewsScanService.RadarQuery(new RadarTopic { Query = "yeni fabrika yatırımı", City = "Manisa" }, "3d"));
        Assert.Equal("yeni fabrika yatırımı when:3d",
            NewsScanService.RadarQuery(new RadarTopic { Query = "yeni fabrika yatırımı" }, "3d"));
    }
}
