using System.Security.Cryptography;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EgebisLeadFinder.Tests;

/// <summary>Kod incelemesinde bulunan hatalarin duzeltmeleri.</summary>
public class ReviewFixTests
{
    private static ApplicationDbContext Db(string? name = null) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Firma_gorunurlugu_kendi_aramasi_eski_kayit_ve_favoriler()
    {
        await using var db = Db();
        var ali = new AppUser { UserName = "ali", PasswordHash = "x" };
        var ayse = new AppUser { UserName = "ayse", PasswordHash = "x" };
        var aliFirma = new Company { Name = "Ali'nin bulduğu", Domain = "a.com" };
        var ayseFirma = new Company { Name = "Ayşe'nin bulduğu", Domain = "b.com" };
        var eski = new Company { Name = "Eski kayıt", Domain = "c.com" };
        db.AddRange(ali, ayse, aliFirma, ayseFirma, eski);
        await db.SaveChangesAsync();
        var aliRun = new SearchRun { UserId = ali.Id, Industry = "X" };
        var ayseRun = new SearchRun { UserId = ayse.Id, Industry = "Y" };
        db.AddRange(aliRun, ayseRun);
        await db.SaveChangesAsync();
        db.SearchRunCompanies.AddRange(
            new SearchRunCompany { SearchRunId = aliRun.Id, CompanyId = aliFirma.Id },
            new SearchRunCompany { SearchRunId = ayseRun.Id, CompanyId = ayseFirma.Id });
        await db.SaveChangesAsync();

        List<string> Visible(int userId, bool admin = false) =>
            db.Companies.VisibleTo(db, userId, admin).Select(c => c.Name).OrderBy(n => n).ToList();

        Assert.Equal(new[] { "Ali'nin bulduğu", "Eski kayıt" }, Visible(ali.Id));
        Assert.Equal(3, Visible(ali.Id, admin: true).Count);

        // Ayse'nin ozel sonucu Ali'ye kapali: favoriye de eklenemez.
        var favorites = new FavoriteService(db);
        Assert.Null(await favorites.ToggleAsync(ali.Id, ayseFirma.Id));

        // Ayse favorisine ekleyip listesini herkese acinca firma Ali'ye de gorunur.
        Assert.True(await favorites.ToggleAsync(ayse.Id, ayseFirma.Id));
        Assert.DoesNotContain("Ayşe'nin bulduğu", Visible(ali.Id));
        await favorites.SetPublicAsync(ayse.Id, true);
        Assert.Contains("Ayşe'nin bulduğu", Visible(ali.Id));
        Assert.True(await favorites.ToggleAsync(ali.Id, ayseFirma.Id));
    }

    [Fact]
    public void Onekli_duz_metin_de_sifrelenir()
    {
        var enc = new FieldEncryption(RandomNumberGenerator.GetBytes(32));
        var cipher = enc.Encrypt("enc:v1:aslinda-duz-metin")!;

        Assert.NotEqual("enc:v1:aslinda-duz-metin", cipher);
        Assert.Equal("enc:v1:aslinda-duz-metin", enc.Decrypt(cipher));
        Assert.Equal(cipher, enc.Encrypt(cipher)); // gecerli sifreli deger yine iki kez sifrelenmez
    }

    [Fact]
    public async Task Cozulemeyen_gizli_ayar_bos_form_veya_yer_tutucuyla_ezilmez()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName))
            .BuildServiceProvider();
        var settings = new SettingsService(services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), new MemoryCache(new MemoryCacheOptions()), NullLogger<SettingsService>.Instance);

        // Baska bir anahtarla sifrelenmis deger (anahtar dosyasi degismis).
        var foreign = new FieldEncryption(RandomNumberGenerator.GetBytes(32)).Encrypt("gercek-serper-anahtari")!;
        await using (var db = Db(dbName))
        {
            db.AppSettings.Add(new AppSetting { Key = SettingKeys.SerperApiKey, Value = foreign });
            await db.SaveChangesAsync();
        }

        // Servislere yer tutucu metin anahtar diye gitmez.
        Assert.Null(await settings.GetAsync(SettingKeys.SerperApiKey));

        await settings.SetManyAsync(new Dictionary<string, string?> { [SettingKeys.SerperApiKey] = null });
        await settings.SetManyAsync(new Dictionary<string, string?> { [SettingKeys.SerperApiKey] = FieldEncryption.DecryptFailed });
        await using (var db = Db(dbName))
            Assert.Equal(foreign, (await db.AppSettings.SingleAsync()).Value);

        // Yonetici bilerek yeni anahtar girerse yazilir.
        await settings.SetManyAsync(new Dictionary<string, string?> { [SettingKeys.SerperApiKey] = "yeni-anahtar" });
        Assert.Equal("yeni-anahtar", await settings.GetAsync(SettingKeys.SerperApiKey));
    }

    private sealed class FakeInsightAi : IInsightAi
    {
        public Task<CompareAiResult> CompareCompaniesAsync(BusinessProfile? p, string a, string b, CancellationToken ct = default) =>
            Task.FromResult(new CompareAiResult());
        public Task<NaceMatchAiResult> MatchNaceAsync(string keyword, CancellationToken ct = default) =>
            Task.FromResult(new NaceMatchAiResult { Codes = { new("22.22", "Plastik ambalaj", "ambalaj"), new("22", "Plastik", null) } });
        public Task<SectorAiResult> AnalyzeSectorsAsync(BusinessProfile? p, string k, IReadOnlyList<SectorInput> s, CancellationToken ct = default) =>
            Task.FromResult(new SectorAiResult());
        public Task<IcpSuggestion> SuggestIcpAsync(string url, string text, CancellationToken ct = default) =>
            Task.FromResult(new IcpSuggestion());
    }

    [Fact]
    public async Task Nace_eslesmesinde_firma_sayilari_tek_sorguda_dogru_hesaplanir()
    {
        await using var db = Db();
        db.Companies.AddRange(
            new Company { Name = "A", Domain = "a.com", NaceCode = "22.22" },
            new Company { Name = "B", Domain = "b.com", NaceCode = "22.29" },
            new Company { Name = "C", Domain = "c.com", NaceCode = "25.11" },
            new Company { Name = "D", Domain = "d.com" });
        await db.SaveChangesAsync();

        var service = new SectorAnalysisService(db, new FakeInsightAi(), null!, new MemoryCache(new MemoryCacheOptions()));
        var (matches, error) = await service.MatchAsync("plastik ambalaj");

        Assert.Null(error);
        Assert.Equal(1, matches.Single(m => m.Code == "22.22").Companies);
        Assert.Equal(2, matches.Single(m => m.Code == "22").Companies);
    }
}
