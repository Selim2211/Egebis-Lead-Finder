using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>Puanlama anahtar kelimeleri: profilden gelen ölçütler puan verir ve puan dökümünde görünür.</summary>
public class ScoringSignalTests
{
    private static LeadScoringService Scorer() => new(Options.Create(new ScoringOptions()));

    private static List<ScoringSignal> Signals() => new()
    {
        new ScoringSignal { Name = "Altyapı projeleri", Terms = { "altyapı", "şantiye" }, Points = 15 },
        new ScoringSignal { Name = "Büyük ölçek", Terms = { "holding" }, Points = 10 },
        new ScoringSignal { Name = "Ihale deneyimi", Terms = { "ihale" }, Points = 20 }
    };

    private static ScoreBreakdown Score(CompanyAnalysis analysis, IReadOnlyList<ScoringSignal>? signals) =>
        Scorer().ScoreCompany(analysis, null, null, null, new Company { Name = "Akme İnşaat" },
            new FitContext(true, Array.Empty<string>(), signals));

    [Fact]
    public void Satir_bicimi_okunur_ve_yazilir()
    {
        var parsed = ScoringSignal.ParseLines("Üretim yapıyor | fabrika, imalat | 15\nTek satır\n\n| boş ad | 5");

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Üretim yapıyor", parsed[0].Name);
        Assert.Equal(new[] { "fabrika", "imalat" }, parsed[0].Terms);
        Assert.Equal(15, parsed[0].Points);
        Assert.Equal(10, parsed[1].Points);
        Assert.Equal("Üretim yapıyor | fabrika, imalat | 15", ScoringSignal.FormatLines(parsed.Take(1)));
    }

    [Fact]
    public void Temizleme_puani_sinirlar_ve_yinelenenleri_atar()
    {
        var cleaned = ScoringSignal.Clean(new[]
        {
            new ScoringSignal { Name = "A", Points = 99, Terms = { "x", "yy" } },
            new ScoringSignal { Name = "a", Points = 5 },
            new ScoringSignal { Name = " ", Points = 5 },
            new ScoringSignal { Name = "B", Points = 0 }
        });

        Assert.Equal(2, cleaned.Count);
        Assert.Equal(ScoringSignal.MaxPoints, cleaned[0].Points);
        Assert.Equal(new[] { "yy" }, cleaned[0].Terms);
        Assert.Equal(ScoringSignal.MinPoints, cleaned[1].Points);
    }

    [Fact]
    public void Terim_analiz_metninde_gecince_puan_verir_ve_dokumde_gorunur()
    {
        var analysis = new CompanyAnalysis { Potential = true, FitScore = 80, Industry = "İnşaat", Products = { "Altyapı projeleri" } };

        var breakdown = Score(analysis, Signals());

        Assert.Contains(breakdown.Items, i => i.Reason == "Anahtar kelime: Altyapı projeleri" && i.Points == 15);
        Assert.DoesNotContain(breakdown.Items, i => i.Reason.Contains("Büyük ölçek"));
    }

    [Fact]
    public void Yapay_zeka_olcutu_isaretlediyse_kelime_gecmese_de_puan_verir()
    {
        var analysis = new CompanyAnalysis { Potential = true, FitScore = 80, Industry = "Hizmet", MatchedSignals = { "büyük ölçek" } };

        var breakdown = Score(analysis, Signals());

        Assert.Contains(breakdown.Items, i => i.Reason == "Anahtar kelime: Büyük ölçek" && i.Points == 10);
    }

    [Fact]
    public void Anahtar_kelime_puani_toplamda_25_ile_sinirlidir()
    {
        var analysis = new CompanyAnalysis
        {
            Potential = true, FitScore = 50, Industry = "İnşaat",
            Products = { "altyapı", "ihale hizmetleri" }, MatchedSignals = { "Büyük ölçek" }
        };

        var breakdown = Score(analysis, Signals());

        var signalPoints = breakdown.Items.Where(i => i.Reason.StartsWith("Anahtar kelime:")).Sum(i => i.Points);
        Assert.Equal(LeadScoringService.SignalCap, signalPoints);
    }

    [Fact]
    public void Olcut_varken_uygunluk_agirligi_azalir_olcut_yokken_eskisi_gibidir()
    {
        var analysis = new CompanyAnalysis { Potential = true, FitScore = 100, Industry = "Hizmet" };

        var without = Score(analysis, null).Items.Single(i => i.Reason.StartsWith("Uygunluk"));
        var with = Score(analysis, Signals()).Items.Single(i => i.Reason.StartsWith("Uygunluk"));

        Assert.Equal(50, without.Points);
        Assert.Equal(35, with.Points);
    }

    [Fact]
    public void Analiz_istemi_olcutleri_ve_matchedSignals_alanini_icerir()
    {
        var profile = new BusinessProfile
        {
            Offering = "Boru ve vana", IdealCustomer = "Tesisat firmaları",
            ScoringSignals = { new ScoringSignal { Name = "Altyapı projeleri", Terms = { "altyapı", "şantiye" }, Points = 15 } }
        };

        var prompt = GeminiAiService.AnalysisPrompt(profile);

        Assert.Contains("PUANLAMA ÖLÇÜTLERİ", prompt);
        Assert.Contains("Altyapı projeleri", prompt);
        Assert.Contains("matchedSignals", prompt);
        Assert.DoesNotContain("PUANLAMA ÖLÇÜTLERİ", GeminiAiService.AnalysisPrompt(new BusinessProfile { Offering = "x", IdealCustomer = "y" }));
    }
}
