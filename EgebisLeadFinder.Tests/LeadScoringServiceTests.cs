using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

public class LeadScoringServiceTests
{
    // appsettings.json ile ayni agirliklar.
    private static LeadScoringService CreateService() =>
        new(Options.Create(new ScoringOptions
        {
            SapFound = 35,
            Manufacturer = 25,
            TargetIndustry = 15,
            ItManagerFound = 10,
            EmailFound = 10,
            LargeCompany = 5,
            TargetIndustryKeywords = new List<string> { "otomotiv", "makina", "metal" },
            TitleScores = new Dictionary<string, int>
            {
                ["cio"] = 100,
                ["bilgi işlem müdürü"] = 95,
                ["it director"] = 90,
                ["sap"] = 90,
                ["it manager"] = 80,
                ["bilgi işlem"] = 70,
                ["üretim müdürü"] = 70,
                ["genel müdür"] = 60,
                ["satın alma"] = 40
            }
        }));

    [Fact]
    public void Sap_kullanan_uretici_en_yuksek_puani_alir()
    {
        var service = CreateService();

        var analysis = new CompanyAnalysis
        {
            Potential = true,
            Sap = "yes",
            Manufacturer = true,
            Industry = "Otomotiv Yan Sanayi",
            EmployeeSizeHint = "500 çalışan"
        };

        var contacts = new List<Contact>
        {
            new() { Title = "Bilgi İşlem Müdürü", TitleScore = 95, Email = "it@firma.com" }
        };

        var score = service.ScoreCompany(analysis, site: null, contacts);

        // 35 + 25 + 15 + 5 + 10 + 10 = 100
        Assert.Equal(100, score.Total);
    }

    [Fact]
    public void Sap_ihtimali_tam_puanin_yarisini_verir()
    {
        var service = CreateService();

        var kesin = service.ScoreCompany(
            new CompanyAnalysis { Potential = true, Manufacturer = true, Sap = "yes" }, null, null);
        var ihtimal = service.ScoreCompany(
            new CompanyAnalysis { Potential = true, Manufacturer = true, Sap = "likely" }, null, null);

        Assert.Equal(60, kesin.Total);   // 35 SAP + 25 üretici
        Assert.Equal(42, ihtimal.Total); // 17 SAP + 25 üretici
    }

    [Fact]
    public void Sap_kullanmayan_firma_sap_puani_almaz()
    {
        var service = CreateService();

        var score = service.ScoreCompany(
            new CompanyAnalysis { Potential = true, Sap = "no", Manufacturer = true }, null, null);

        Assert.Equal(25, score.Total);
        Assert.DoesNotContain(score.Items, i => i.Reason.Contains("SAP"));
    }

    [Fact]
    public void Uretici_olmayan_firma_sap_puani_almaz()
    {
        // "SAP" aramasi SAP'tan soz eden yazilim/danismanlik sitelerini de getirir;
        // bunlar SAP kullanan uretici degildir.
        var service = CreateService();

        var score = service.ScoreCompany(
            new CompanyAnalysis { Potential = true, Sap = "yes", Manufacturer = false }, null, null);

        Assert.Equal(0, score.Total);
    }

    [Fact]
    public void Hedef_disi_firma_elenir()
    {
        var service = CreateService();

        var analysis = new CompanyAnalysis
        {
            Potential = false,
            Reason = "Haber sitesi",
            Sap = "yes",
            Manufacturer = true,
            Industry = "Otomotiv"
        };

        var score = service.ScoreCompany(analysis, null, null);

        Assert.Equal(0, score.Total);
        Assert.Equal("Haber sitesi", score.DisqualifiedReason);
    }

    [Fact]
    public void Sap_saticisi_rakip_olarak_elenir()
    {
        var service = CreateService();

        var analysis = new CompanyAnalysis
        {
            Potential = true,
            SapVendor = true,
            Sap = "yes",
            Manufacturer = true,
            Industry = "Otomotiv"
        };

        var score = service.ScoreCompany(analysis, null, null);

        Assert.Equal(0, score.Total);
        Assert.Contains("rakip", score.DisqualifiedReason);
    }

    [Fact]
    public void Puan_100u_asamaz()
    {
        var service = CreateService();

        var analysis = new CompanyAnalysis
        {
            Potential = true,
            Sap = "yes",
            Manufacturer = true,
            Industry = "Otomotiv",
            EmployeeSizeHint = "10.000 çalışan"
        };

        var contacts = new List<Contact>
        {
            new() { TitleScore = 100, Email = "cio@firma.com" }
        };

        Assert.Equal(100, service.ScoreCompany(analysis, null, contacts).Total);
    }

    [Fact]
    public void Analiz_yoksa_akis_kirilmaz()
    {
        var service = CreateService();

        var site = new ScrapedSite { Emails = { "info@firma.com" } };
        var score = service.ScoreCompany(analysis: null, site, contacts: null);

        Assert.Equal(10, score.Total);
    }

    [Theory]
    [InlineData("CIO", 100)]
    [InlineData("Bilgi İşlem Müdürü", 95)]
    [InlineData("SAP Danışmanı", 90)]
    [InlineData("IT Manager", 80)]
    [InlineData("Üretim Müdürü", 70)]
    [InlineData("Satın Alma Uzmanı", 40)]
    [InlineData("Aşçıbaşı", 0)]
    [InlineData(null, 0)]
    public void Unvan_puanlari_beklendigi_gibi(string? title, int expected)
    {
        Assert.Equal(expected, CreateService().ScoreTitle(title));
    }

    [Theory]
    // Gercek verilerde gorulen yanlis eslesmeler: "Automocion" icinde "cio",
    // "Directors" icinde "cto", "Ceoğrafya" benzeri kelimelerde "ceo".
    [InlineData("Gestamp Automocion firması ile ortaklık")]
    [InlineData("Board of Directors")]
    [InlineData("Executive Committee")]
    [InlineData("Sapanca Tesisleri")]
    public void Kelime_icinde_kalan_kisaltmalar_puan_almaz(string title)
    {
        Assert.Equal(0, CreateService().ScoreTitle(title));
    }

    [Fact]
    public void Kelime_sinirindaki_kisaltma_puan_alir()
    {
        var service = CreateService();

        Assert.Equal(100, service.ScoreTitle("CIO"));
        Assert.Equal(100, service.ScoreTitle("Grup CIO'su"));
        Assert.Equal(90, service.ScoreTitle("SAP Danışmanı"));
    }

    [Fact]
    public void Uzun_unvan_esleşmesi_kisa_olani_yener()
    {
        // "bilgi işlem müdürü" (95), "bilgi işlem" (70) icinde geciyor; spesifik olan kazanmali.
        Assert.Equal(95, CreateService().ScoreTitle("Bilgi İşlem Müdürü"));
    }

    [Fact]
    public void En_uygun_muhatap_unvan_puanina_gore_secilir()
    {
        var service = CreateService();

        var contacts = new List<Contact>
        {
            new() { Name = "Ali Veli", Title = "Satın Alma", TitleScore = 40 },
            new() { Name = "Ayşe Kaya", Title = "Bilgi İşlem Müdürü", TitleScore = 95 },
            new() { Name = "Can Demir", Title = "Üretim Müdürü", TitleScore = 70 }
        };

        Assert.Equal("Ayşe Kaya", service.PickBestContact(contacts)!.Name);
    }

    [Fact]
    public void Esit_unvanda_e_postasi_olan_tercih_edilir()
    {
        var service = CreateService();

        var contacts = new List<Contact>
        {
            new() { Name = "Postasız", TitleScore = 90 },
            new() { Name = "Postalı", TitleScore = 90, Email = "a@b.com" }
        };

        Assert.Equal("Postalı", service.PickBestContact(contacts)!.Name);
    }
}
