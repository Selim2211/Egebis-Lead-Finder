using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>Ayarlarda unvan bos birakilirsa Apollo'dan firmadaki tum kisiler gelir (unvan/kidem filtresi yok).</summary>
public class AllPeopleTests
{
    private static SettingsService CreateSettings(string dbName) =>
        new(new ServiceCollection()
                .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName))
                .BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), new MemoryCache(new MemoryCacheOptions()), NullLogger<SettingsService>.Instance);

    [Fact]
    public async Task Kaydedilmemis_unvan_varsayilana_bos_birakilmis_unvan_tum_kisilere_doner()
    {
        var settings = CreateSettings(Guid.NewGuid().ToString());

        // Hic kaydedilmediyse varsayilan unvanlar.
        Assert.Equal(SettingsService.DefaultTitleKeywords.Length, (await settings.GetTitleKeywordsAsync()).Count);

        // Bilerek bos birakildi ("*"): filtre yok.
        await settings.SetManyAsync(new Dictionary<string, string?> { [SettingKeys.LeadTitleKeywords] = SettingsService.AllPeopleMarker });
        Assert.Empty(await settings.GetTitleKeywordsAsync());

        // Yeniden unvan yazilirsa filtre geri gelir.
        await settings.SetManyAsync(new Dictionary<string, string?> { [SettingKeys.LeadTitleKeywords] = "Satın Alma, Genel Müdür" });
        Assert.Equal(new[] { "Satın Alma", "Genel Müdür" }, await settings.GetTitleKeywordsAsync());
    }

[Fact]
    public void Segment_unvani_bos_ayari_filtreye_cevirmez()
    {
        var profile = new BusinessProfile
        {
            Offering = "Boru", IdealCustomer = "Tesisat",
            Segments = { new TargetSegment { Name = "Tesisat", TargetTitles = { "Satın Alma" } } }
        };

        Assert.Empty(HybridContactEnrichmentService.SegmentTitlesFirst(new Company { FitSegment = "Tesisat" }, profile, new List<string>()));
    }
}
