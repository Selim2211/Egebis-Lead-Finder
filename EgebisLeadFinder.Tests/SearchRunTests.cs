using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Tests;

/// <summary>Madde 8: Firmalar ekrani arama bazli; her arama firmalarini (yeni + bilinen) kaydeder.</summary>
public class SearchRunTests
{
    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Company Known, Company New)> SeedCompaniesAsync(ApplicationDbContext db)
    {
        var known = new Company { Name = "Eski A.Ş.", Domain = "eski.com" };
        var fresh = new Company { Name = "Yeni A.Ş.", Domain = "yeni.com" };
        db.Companies.AddRange(known, fresh);
        await db.SaveChangesAsync();
        return (known, fresh);
    }

    [Fact]
    public async Task Arama_yeni_ve_bilinen_firmalari_baglar()
    {
        await using var db = Db();
        var (known, fresh) = await SeedCompaniesAsync(db);
        var service = new SearchRunService(db);

        var run = await service.StartAsync(new SearchCriteria { Industry = "Otomotiv", RegionKey = "DE" }, null, userId: 3);
        await service.CompleteAsync(run.Id, new DiscoveryResult
        {
            FoundBySearch = 2,
            AlreadyKnown = 1,
            Companies = { known, fresh },
            NewCompanyIds = { fresh.Id }
        });

        var links = await db.SearchRunCompanies.Where(x => x.SearchRunId == run.Id).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.True(links.Single(l => l.CompanyId == fresh.Id).IsNew);
        Assert.False(links.Single(l => l.CompanyId == known.Id).IsNew);

        var saved = await db.SearchRuns.SingleAsync();
        Assert.Equal(SearchRunStatus.Done, saved.Status);
        Assert.Equal(1, saved.NewCount);
        Assert.Equal(SearchRunKind.Sector, saved.Kind);
        Assert.Equal("Otomotiv · Almanya", saved.Describe());
    }

    [Fact]
    public async Task Profil_aramasi_firmalari_profile_otomatik_ekler()
    {
        await using var db = Db();
        var (known, fresh) = await SeedCompaniesAsync(db);
        var profile = new SearchProfile { Name = "Ege Otomotiv", Industry = "Otomotiv" };
        db.SearchProfiles.Add(profile);
        db.CompanySearchProfiles.Add(new CompanySearchProfile { Company = known, SearchProfile = profile });
        await db.SaveChangesAsync();

        var service = new SearchRunService(db);
        var run = await service.StartAsync(new SearchCriteria { Industry = "Otomotiv" }, profile, null);
        await service.CompleteAsync(run.Id, new DiscoveryResult { Companies = { known, fresh }, NewCompanyIds = { fresh.Id } });

        Assert.Equal(SearchRunKind.Profile, (await db.SearchRuns.SingleAsync()).Kind);
        Assert.Equal(2, await db.CompanySearchProfiles.CountAsync(x => x.SearchProfileId == profile.Id));
        Assert.Contains("(profil: Ege Otomotiv)", run.Describe());
    }

    [Fact]
    public async Task Iptal_ve_yarida_kalan_durumlar_yazilir()
    {
        await using var db = Db();
        var service = new SearchRunService(db);

        var cancelled = await service.StartAsync(new SearchCriteria { Industry = "Metal" }, null, null);
        await service.CompleteAsync(cancelled.Id, new DiscoveryResult { Cancelled = true });
        var aborted = await service.StartAsync(new SearchCriteria { Industry = "Metal" }, null, null);
        await service.CompleteAsync(aborted.Id, new DiscoveryResult { AbortReason = "Serper kotası doldu" });
        var failed = await service.StartAsync(new SearchCriteria { Industry = "Metal" }, null, null);
        await service.FailAsync(failed.Id, "Bağlantı hatası");

        Assert.Equal(SearchRunStatus.Cancelled, (await db.SearchRuns.FindAsync(cancelled.Id))!.Status);
        Assert.Equal(SearchRunStatus.Aborted, (await db.SearchRuns.FindAsync(aborted.Id))!.Status);
        Assert.Equal(SearchRunStatus.Failed, (await db.SearchRuns.FindAsync(failed.Id))!.Status);
    }

    [Fact]
    public async Task Varsayilan_arama_kullanicinin_son_sonuclu_aramasidir()
    {
        await using var db = Db();
        var (known, _) = await SeedCompaniesAsync(db);
        var service = new SearchRunService(db);

        var mine = await service.StartAsync(new SearchCriteria { Industry = "A" }, null, userId: 1);
        await service.CompleteAsync(mine.Id, new DiscoveryResult { Companies = { known } });
        var others = await service.StartAsync(new SearchCriteria { Industry = "B" }, null, userId: 2);
        await service.CompleteAsync(others.Id, new DiscoveryResult { Companies = { known } });
        // Sonucsuz arama varsayilan olmaz.
        var empty = await service.StartAsync(new SearchCriteria { Industry = "C" }, null, userId: 1);
        await service.CompleteAsync(empty.Id, new DiscoveryResult());

        Assert.Equal(mine.Id, await service.DefaultRunIdAsync(1));
        Assert.Equal(others.Id, await service.DefaultRunIdAsync(99));
        Assert.Equal(others.Id, await service.DefaultRunIdAsync(null));
    }

    [Fact]
    public void Uzun_suren_calisiyor_kaydi_yarida_kaldi_sayilir()
    {
        var now = DateTime.UtcNow;
        var run = new SearchRun { StartedAt = now.AddHours(-3), Status = SearchRunStatus.Running };
        Assert.True(run.IsStale(now));
        Assert.Equal("yarıda kaldı", run.StatusLabel(now));
    }
}
