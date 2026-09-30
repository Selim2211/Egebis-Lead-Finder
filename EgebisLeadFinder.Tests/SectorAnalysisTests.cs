using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

/// <summary>Faz-II madde 4: anahtar kelimeden NACE eslesme ve sektor raporu.</summary>
public class SectorAnalysisTests
{
    [Fact]
    public void Anahtar_kelime_katalogdaki_bolum_adiyla_eslesir()
    {
        var matches = SectorAnalysisService.LocalMatches("Plastik ürünler");
        Assert.Contains(matches, d => d.Code == "22");

        Assert.Contains(SectorAnalysisService.LocalMatches("gıda"), d => d.Code == "10");
        Assert.Equal("22", Assert.Single(SectorAnalysisService.LocalMatches("22.22")).Code);
        Assert.Empty(SectorAnalysisService.LocalMatches("xy"));
    }

    [Fact]
    public void Yapay_zeka_nace_onerileri_dogrulanir_uydurma_kod_atilir()
    {
        var json = """
            {"codes":[{"code":"22.22","name":"Plastik ambalaj malzemelerinin imalatı","reason":"ambalaj"},
                      {"code":"2222","name":"tekrar"},
                      {"code":"00.00","name":"uydurma"},
                      {"code":"17.21","name":"Kağıt ambalaj"}]}
            """;
        var result = GeminiAiService.ParseNaceMatches(json);

        Assert.Equal(new[] { "22.22", "17.21" }, result.Codes.Select(c => c.Code));
    }

    [Fact]
    public void Secim_kod_ad_ciftlerinden_cozulur()
    {
        var sel = SectorAnalysisService.ParseSelection(new[] { "22.22|Plastik ambalaj", "22.22|tekrar", "abc|x", "10" });
        Assert.Equal(2, sel.Count);
        Assert.Equal(("22.22", "Plastik ambalaj"), sel[0]);
        Assert.Equal("Gıda ürünlerinin imalatı", sel[1].Name);
    }

    [Fact]
    public void Sektor_sonucu_istenen_sirayla_ve_sinirlanmis_puanla_doner()
    {
        var inputs = new List<SectorInput>
        {
            new("22.22", "Plastik ambalaj", new SectorStats(4, 50, 70, 1, 2, 0)),
            new("10", "Gıda", new SectorStats(0, 0, null, 0, 0, 0))
        };
        var json = """
            {"overview":"Plastik öncelikli.","sectors":[
              {"code":"10","fitScore":20,"verdict":"uygun_degil","summary":"düşük"},
              {"code":"22.22","fitScore":140,"verdict":"harika","summary":"yüksek","reasons":["a"]}]}
            """;
        var result = GeminiAiService.ParseSectorResult(json, inputs);

        Assert.True(result.Success);
        Assert.Equal(new[] { "22.22", "10" }, result.Sectors.Select(s => s.Code));
        Assert.Equal(100, result.Sectors[0].FitScore);
        Assert.Equal("kismen", result.Sectors[0].Verdict);
        Assert.Equal(4, result.Sectors[0].Stats!.Companies);

        var report = new SectorReport { Keyword = "plastik", Codes = "22.22,10", UserName = "Ali" };
        var tables = SectorAnalysisService.ToExport(report, result);
        Assert.Equal(3, tables.Count);
        Assert.Equal(2, tables[0].Rows.Count);
        Assert.NotEmpty(ExportService.ToXlsx(tables));
    }
}
