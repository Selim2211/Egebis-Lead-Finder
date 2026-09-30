using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>3. asama: "Bize uygun mu?" puani, segment, neden ve segmente gore unvanlar.</summary>
public class FitScoringTests
{
    private static LeadScoringService Scoring() => new(Options.Create(new ScoringOptions()));

    private static CompanyAnalysis Analysis(int? fit = 80, bool potential = true, bool vendor = false) => new()
    {
        Industry = "Plastik", Manufacturer = true, Sap = "yes", Potential = potential, SapVendor = vendor,
        FitScore = fit, Segment = "Plastik enjeksiyon", Reason = "Seri üretim yapıyor, robot hücresine uygun."
    };

    private static readonly FitContext Active = new(true, new[] { "Kalite Müdürü" });

    private static BusinessProfile Profile() => new()
    {
        Offering = "Robot", IdealCustomer = "Fabrika",
        Segments = { new TargetSegment { Name = "Plastik Enjeksiyon", TargetTitles = { "Kalıphane Şefi" } } }
    };

    [Fact]
    public void Profil_modunda_SAP_ve_uretici_puani_yerine_uygunluk_puani_verilir()
    {
        var breakdown = Scoring().ScoreCompany(Analysis(fit: 80), null, null, null, new Company(), Active);

        var fit = Assert.Single(breakdown.Items, i => i.Reason.StartsWith("Uygunluk"));
        Assert.Equal("Uygunluk (yapay zekâ): %80", fit.Reason);
        Assert.Equal(40, fit.Points); // (SAP 20 + üretici 30) * 0.8
        Assert.DoesNotContain(breakdown.Items, i => i.Reason.Contains("SAP") || i.Reason == "Üretici firma");
    }

    [Fact]
    public void Uygunluk_puani_olmayan_eski_analizde_ve_profil_yokken_eski_puanlama_kalir()
    {
        var old = Scoring().ScoreCompany(Analysis(fit: null), null, null, null, new Company(), Active);
        var noProfile = Scoring().ScoreCompany(Analysis(fit: 90), null, null, null, new Company(), FitContext.Inactive);

        foreach (var b in new[] { old, noProfile })
        {
            Assert.Contains(b.Items, i => i.Reason == "SAP kullanımı doğrulandı");
            Assert.Contains(b.Items, i => i.Reason == "Üretici firma");
            Assert.DoesNotContain(b.Items, i => i.Reason.StartsWith("Uygunluk"));
        }
    }

    [Fact]
    public void Profil_modunda_rakip_ve_hedef_disi_mesajlari_genel()
    {
        var rival = Scoring().ScoreCompany(Analysis(vendor: true), null, null, null, new Company(), Active);
        var outOfTarget = new CompanyAnalysis { Potential = false };
        var off = Scoring().ScoreCompany(outOfTarget, null, null, null, new Company(), Active);

        Assert.Equal("Rakip firma (bizimle aynı işi yapıyor)", rival.DisqualifiedReason);
        Assert.Equal("Hedef müşteri tanımımıza uymuyor", off.DisqualifiedReason);
    }

    [Fact]
    public void Profil_modunda_hedef_unvandaki_kisi_puan_getirir()
    {
        var contacts = new[] { new Contact { Title = "Kalite Müdürü", TitleScore = 0 } };

        var withProfile = Scoring().ScoreCompany(Analysis(), null, contacts, null, new Company(), Active);
        var legacy = Scoring().ScoreCompany(Analysis(), null, contacts, null, new Company());

        Assert.Contains(withProfile.Items, i => i.Reason == "Hedef unvanda kişi bulundu");
        Assert.DoesNotContain(legacy.Items, i => i.Reason.Contains("yöneticisi bulundu")); // TitleScore 0: IT/SAP degil
    }

    [Fact]
    public void Uygunluk_segment_ve_neden_firmaya_yazilir()
    {
        var company = new Company();
        IcpService.ApplyFit(company, Analysis(fit: 85), Profile());

        Assert.Equal(85, company.FitScore);
        Assert.Equal("Plastik Enjeksiyon", company.FitSegment); // profildeki yazimla eslesir
        Assert.Equal("Seri üretim yapıyor, robot hücresine uygun.", company.FitReason);
    }

    [Fact]
    public void Hedef_disi_firmada_uygunluk_dusuk_tutulur_segment_yazilmaz()
    {
        var company = new Company();
        IcpService.ApplyFit(company, Analysis(fit: 70, potential: false), Profile());

        Assert.Equal(19, company.FitScore);
        Assert.Null(company.FitSegment);
        Assert.NotNull(company.FitReason);
    }

    [Fact]
    public void Profil_yokken_uygunluk_bos_ama_neden_yazilir()
    {
        var company = new Company { FitScore = 50, FitSegment = "Eski" };
        IcpService.ApplyFit(company, Analysis(fit: 90), new BusinessProfile());

        Assert.Null(company.FitScore);
        Assert.Null(company.FitSegment);
        Assert.NotNull(company.FitReason);
    }

    [Fact]
    public void Bilinmeyen_segment_adi_eslesmez()
    {
        var company = new Company();
        var analysis = Analysis();
        analysis.Segment = "Gıda";
        IcpService.ApplyFit(company, analysis, Profile());

        Assert.Null(company.FitSegment);
        Assert.Equal(80, company.FitScore);
    }

    [Fact]
    public void Segmentin_unvanlari_Apollo_aramasinda_one_alinir()
    {
        var general = new List<string> { "Genel Müdür", "kalıphane şefi" };

        var inSegment = HybridContactEnrichmentService.SegmentTitlesFirst(new Company { FitSegment = "Plastik Enjeksiyon" }, Profile(), general);
        var noSegment = HybridContactEnrichmentService.SegmentTitlesFirst(new Company(), Profile(), general);

        Assert.Equal(new[] { "Kalıphane Şefi", "Genel Müdür" }, inSegment);
        Assert.Same(general, noSegment);
    }

    [Theory]
    [InlineData(90, "Çok uygun", "fit-high")]
    [InlineData(65, "Uygun", "fit-good")]
    [InlineData(45, "Kısmen uygun", "fit-mid")]
    [InlineData(25, "Zayıf uyum", "fit-low")]
    [InlineData(5, "Uygun değil", "fit-low")]
    public void Uygunluk_etiketleri(int score, string label, string css)
    {
        Assert.Equal(label, FitDisplay.Label(score));
        Assert.Equal(css, FitDisplay.Css(score));
    }

    [Fact]
    public void Analiz_yaniti_uygunluk_ve_segmenti_tasir()
    {
        var analysis = System.Text.Json.JsonSerializer.Deserialize<CompanyAnalysis>(
            """{"potential":true,"fitScore":72,"segment":"Metal","reason":"r"}""")!;

        Assert.Equal(72, analysis.FitScore);
        Assert.Equal("Metal", analysis.Segment);
        Assert.Contains("fitScore alanı", GeminiAiService.AnalysisPrompt(Profile()));
        Assert.DoesNotContain("fitScore alanı", GeminiAiService.AnalysisPrompt(null));
    }
}
