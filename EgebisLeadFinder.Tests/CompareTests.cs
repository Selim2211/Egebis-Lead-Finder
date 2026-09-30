using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

/// <summary>Faz-II madde 3: iki firma karsilastirma.</summary>
public class CompareTests
{
    [Fact]
    public void Onde_olan_once_lead_puanina_sonra_uygunluga_gore_belirlenir()
    {
        var a = new Company { Name = "A", Score = 72, FitScore = 40 };
        var b = new Company { Name = "B", Score = 55, FitScore = 90 };
        Assert.Equal(CompareSide.A, CompanyComparisonService.DecideLeader(a, b).Leader);

        b.Score = 72;
        Assert.Equal(CompareSide.B, CompanyComparisonService.DecideLeader(a, b).Leader);

        b.FitScore = 40;
        Assert.Equal(CompareSide.None, CompanyComparisonService.DecideLeader(a, b).Leader);
    }

    [Fact]
    public void Yapay_zeka_yaniti_cozulur_ve_oneri_normalize_edilir()
    {
        var json = """
            {"recommended":"b","headline":"B daha uygun","summary":"...","aStrengths":["x"],"bStrengths":["y","z"],
             "aRisks":[],"bRisks":["veri az"],"nextSteps":["ara"]}
            """;
        var result = GeminiAiService.ParseCompareResult(json);

        Assert.True(result.Success);
        Assert.Equal("B", result.Recommended);
        Assert.Equal(2, result.BStrengths.Count);
        Assert.Equal("esit", GeminiAiService.ParseCompareResult("""{"recommended":"belki"}""").Recommended);
        Assert.False(GeminiAiService.ParseCompareResult("bozuk").Success);
    }

    [Fact]
    public void Excel_ciktisi_tablo_ve_yapay_zeka_sayfasini_icerir()
    {
        var c = new CompanyComparison
        {
            A = new Company { Name = "Ege Plastik" },
            B = new Company { Name = "Marmara Metal" },
            Leader = CompareSide.A,
            LeaderReason = "Lead puanı daha yüksek."
        };
        c.Sections.Add(new CompareSection("Kriterlerimize uygunluk", null, new List<CompareRow>
        {
            new("Lead puanı", "72", "55", CompareSide.A)
        }));
        c.Criteria.Add("Üretici firma olmalı");

        var tables = CompanyComparisonService.ToExport(c);
        Assert.Equal(2, tables.Count);
        Assert.Equal(new[] { "Bölüm", "Kriter", "Ege Plastik", "Marmara Metal", "Önde" }, tables[0].Headers);
        Assert.Equal("Ege Plastik", tables[0].Rows[0][4]);

        c.Ai = new CompareAiResult { Recommended = "A", Headline = "A önde", Summary = "..." , NextSteps = { "Ara" } };
        tables = CompanyComparisonService.ToExport(c);
        Assert.Equal(3, tables.Count);
        Assert.NotEmpty(ExportService.ToXlsx(tables));
    }
}
