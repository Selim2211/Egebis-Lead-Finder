using System.Text.Json;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>"Biz ne arıyoruz?": sirket profili yapay zeka talimatlarini, segmentler ICP'yi besler.</summary>
public class BusinessProfileTests
{
    private static BusinessProfile Configured() => new()
    {
        CompanyName = "Akme Robotik",
        Offering = "Kaynak robotu ve robot hücresi satışı",
        IdealCustomer = "Metal işleyen, seri üretim yapan fabrikalar",
        NotCustomers = "Bayi, hurdacı",
        Competitors = "Robot entegratörleri",
        Segments = { new TargetSegment { Name = "Metal eşya", Description = "Kaynak yoğun üretim" } }
    };

    [Fact]
    public void Profil_bossa_eski_Egebis_talimati_aynen_kullanilir()
    {
        var prompt = GeminiAiService.AnalysisPrompt(new BusinessProfile());

        Assert.StartsWith("Egebis Bilişim için potansiyel müşteri analizi yapıyorsun.", prompt);
        Assert.Contains("Bunlar Egebis'in rakibidir", prompt);
        Assert.Contains("SAP tespiti kuralları", prompt);
        Assert.Contains("Egebis Bilişim adına", GeminiAiService.EmailPrompt(null));
        Assert.Contains("Egebis açısından önemi", GeminiAiService.RatingPrompt(null));
    }

    [Fact]
    public void Profil_doluysa_talimatlar_sirketin_kendi_tanimini_kullanir()
    {
        var profile = Configured();

        foreach (var prompt in new[]
                 {
                     GeminiAiService.AnalysisPrompt(profile), GeminiAiService.EmailPrompt(profile), GeminiAiService.RatingPrompt(profile)
                 })
        {
            Assert.DoesNotContain("Egebis", prompt);
            Assert.Contains("Akme Robotik", prompt);
            Assert.Contains("Kaynak robotu ve robot hücresi satışı", prompt);
            Assert.Contains("Metal eşya: Kaynak yoğun üretim", prompt);
        }

        var analysis = GeminiAiService.AnalysisPrompt(profile);
        Assert.Contains("MÜŞTERİMİZ OLMAYANLAR: Bayi, hurdacı", analysis);
        Assert.Contains("RAKİPLERİMİZ (müşteri değil): Robot entegratörleri", analysis);
        Assert.Contains("companyName alanı", analysis);
    }

    [Fact]
    public void Eksik_profil_yapilandirilmis_sayilmaz()
    {
        Assert.False(new BusinessProfile { Offering = "X" }.IsConfigured);
        Assert.False(new BusinessProfile { IdealCustomer = "Y" }.IsConfigured);
        Assert.True(new BusinessProfile { Offering = "X", IdealCustomer = "Y" }.IsConfigured);
    }

    [Fact]
    public void Mail_notunda_gonderen_sirket_profilden_gelir()
    {
        var input = new EmailDraftInput { Company = new Company { Name = "Hedef A.Ş." }, SenderName = "Ayşe" };

        Assert.Contains("GÖNDEREN: Ayşe (Egebis Bilişim)", GeminiAiService.BuildEmailBrief(input));
        Assert.Contains("GÖNDEREN: Ayşe (Akme Robotik)", GeminiAiService.BuildEmailBrief(input, Configured()));
    }

    [Fact]
    public void Bozuk_ayar_bos_profil_doner()
    {
        Assert.False(BusinessProfileService.Parse("{bozuk").IsConfigured);
        Assert.False(BusinessProfileService.Parse(null).IsConfigured);

        var roundTrip = BusinessProfileService.Parse(JsonSerializer.Serialize(Configured()));
        Assert.True(roundTrip.IsConfigured);
        Assert.Empty(roundTrip.Segments); // eski kayitlardaki hedef segmentler yok sayilir
    }

    [Fact]
    public void Normalize_hedef_segmentleri_temizler()
    {
        var profile = new BusinessProfile
        {
            Offering = "  X  ",
            Segments = { new TargetSegment { Id = "a", Name = "Bir", RegionKey = "DE" } }
        };

        BusinessProfileService.Normalize(profile);

        Assert.Equal("X", profile.Offering);
        Assert.Empty(profile.Segments);
    }

    [Theory]
    [InlineData("egebis.com", "https://egebis.com")]
    [InlineData("https://www.egebis.com/tr/", "https://www.egebis.com/tr")]
    [InlineData("localhost", null)]
    [InlineData("", null)]
    public void Site_adresi_normallestirilir(string input, string? expected) =>
        Assert.Equal(expected, BusinessProfileService.NormalizeUrl(input));

    [Fact]
    public void Yapay_zeka_taslagi_cozulur()
    {
        var json = """
            {"companyName":"Akme","offering":"Robot","idealCustomer":"Fabrikalar",
             "segments":[{"name":"Metal","description":"d","searchTerm":"Metal eşya","region":"TR","keywords":["kaynak"],"nace":["25"],"exclude":["bayi"]}],
             "targetTitles":["Genel Müdür","genel müdür","Plant Manager"]}
            """;

        var draft = GeminiAiService.ParseBusinessProfileDraft(json);

        Assert.True(draft.Success);
        Assert.True(draft.Profile.IsConfigured);
        var seg = Assert.Single(draft.Profile.Segments);
        Assert.Equal("Metal eşya", seg.EffectiveSearchTerm);
        Assert.Equal("TR", seg.RegionKey);
        Assert.Equal(new[] { "25" }, seg.NaceCodes);
        Assert.Equal(new[] { "Genel Müdür", "Plant Manager" }, draft.TargetTitles);
        Assert.False(GeminiAiService.ParseBusinessProfileDraft("bozuk").Success);
    }

    [Fact]
    public async Task Kaydet_unvanlari_gunceller()
    {
        var settings = new FakeSettingsService();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var icp = new IcpService(settings, new LeadScoringService(Options.Create(new ScoringOptions())), db, null!);
        await icp.SaveAsync(new IcpProfile { NaceCodes = { "25" }, ExcludeKeywords = { "bayi" } });
        var service = new BusinessProfileService(settings, icp, null!, null!);

        var profile = Configured();

        await service.SaveAsync(profile, new[] { "Genel Müdür", " ", "Üretim Müdürü" }, "admin");

        Assert.Equal("Genel Müdür, Üretim Müdürü", await settings.GetAsync(SettingKeys.LeadTitleKeywords));
        var saved = await service.GetAsync();
        Assert.Equal("admin", saved.UpdatedBy);
        Assert.NotNull(saved.UpdatedAt);
        Assert.Empty(saved.Segments);
    }
}
