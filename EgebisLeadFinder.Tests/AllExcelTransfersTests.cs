using ClosedXML.Excel;
using EgebisLeadFinder.Controllers;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Uygulamadaki tüm Excel içe/dışa aktarımları zorlayıcı veriyle: arama profilleri (dışa aktar → geri yükle kayıpsız),
/// karşılaştırma, sektör analizi, audit; ortak Excel üreticisinin sayı/tarih/sayfa adı kuralları.
/// </summary>
public class AllExcelTransfersTests
{
    private const string Dirty = "Satır1\nSatır2; \"tırnak\" \u000B \u0001 \uD800 ✓ 🚀";

    // ---------------- Arama profilleri ----------------

    private static List<SearchProfile> Profiles() => new()
    {
        new() { Name = "Tek ülke il", Industry = "Otomotiv yan sanayi", RegionKey = "TR", Country = "Türkiye", City = "İzmir, Manisa", MaxCompanies = 32 },
        new() { Name = "Çok ülke", Industry = "Beyaz eşya", NaceCode = "27.51", NaceProducts = "buzdolabı, fırın; ocak", RegionKey = "DE", Country = "Almanya",
            RegionKeys = "DE,FR,IT,ES", IsPublic = true, MaxCompanies = 60 },
        new() { Name = "Altı haneli NACE", NaceCode = "27.51.08", RegionKey = "US", Country = "Amerika Birleşik Devletleri", City = "Texas" },
        new() { Name = "Baştaki sıfır", NaceCode = "01.11", Industry = "Tarım", RegionKey = "TR", Country = "Türkiye" },
        new() { Name = "Sonda sıfır", NaceCode = "10.20", Industry = "Balık işleme", RegionKey = "TR", Country = "Türkiye" },
        new() { Name = "=Formül gibi; \"ad\"", Industry = "+Plastik", RegionKey = "TR", Country = "Türkiye", City = "-Bursa" },
        new() { Name = "Eski kayıt (yalnız ülke adı)", Industry = "Makine", Country = "Almanya" },
        new() { Name = "Tüm Avrupa", Industry = "Makine", RegionKey = "DE", RegionKeys = string.Join(",", EgebisLeadFinder.Data.SearchRegions.All.Where(r => r.Group == "Avrupa").Take(30).Select(r => r.Key)) }
    };

    [Fact]
    public void Profiller_disa_aktarilip_geri_yuklenince_hicbir_bilgi_kaybolmaz()
    {
        var profiles = Profiles();
        var rows = SearchProfileTransfer.Parse(new MemoryStream(SearchProfileTransfer.Build(profiles, template: false))).Rows;

        Assert.Equal(profiles.Count, rows.Count);
        for (var i = 0; i < profiles.Count; i++)
        {
            var p = profiles[i];
            var r = rows[i];
            var regions = SearchProfileTransfer.RegionsOf(p);
            Assert.True(r.Error is null, $"{p.Name}: {r.Error}");
            Assert.Equal(p.Name, r.Name);
            Assert.Equal(p.Industry, r.Industry);
            Assert.Equal(p.NaceCode, r.Nace);
            Assert.Equal(p.NaceProducts, r.Products);
            Assert.Equal(regions[0].Key, r.RegionKey);
            Assert.Equal(regions.Count > 1 ? string.Join(",", regions.Select(x => x.Key)) : null, r.RegionKeys);
            Assert.Equal(regions.Count > 1 ? null : p.City, r.City);
            Assert.Equal(p.MaxCompanies, r.MaxCompanies);
            Assert.Equal(p.IsPublic, r.IsPublic);
        }
    }

    [Fact]
    public void Eski_yedi_sutunlu_dosya_ve_yer_degistirmis_sutunlar_dogru_okunur()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Profiller");
        // Eski düzen: Ürünler yok; ayrıca "Herkese açık" ile "Sektör" yer değiştirmiş.
        var headers = new[] { "Profil adı", "Herkese açık", "NACE kodu", "Ülke / bölge", "İl / bölge", "En fazla firma", "Sektör" };
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Cell(2, 1).Value = "Eski dosya"; ws.Cell(2, 2).Value = "Evet"; ws.Cell(2, 3).Value = "28.99";
        ws.Cell(2, 4).Value = "Almanya"; ws.Cell(2, 5).Value = "Bayern"; ws.Cell(2, 6).Value = 12; ws.Cell(2, 7).Value = "Makine";

        var row = Assert.Single(Parse(wb).Rows);
        Assert.Null(row.Error);
        Assert.Equal("Makine", row.Industry);
        Assert.Equal("28.99", row.Nace);
        Assert.Equal("DE", row.RegionKey);
        Assert.Equal("Bayern", row.City);
        Assert.Equal(12, row.MaxCompanies);
        Assert.True(row.IsPublic);
        Assert.Null(row.Products);
    }

    [Theory]
    [InlineData(1.11, "01.11")]
    [InlineData(28.99, "28.99")]
    [InlineData(27.0, "27")]
    [InlineData(27.1, "27.1")]
    [InlineData(2.0, "02")]
    public void Excelin_sayiya_cevirdigi_NACE_kodu_dogru_okunur(double typed, string expected)
    {
        var wb = Template(row: ("Sayı NACE", "Sektör", null));
        wb.Worksheet("Profiller").Cell(2, 3).Value = typed;

        var row = Assert.Single(Parse(wb).Rows);
        Assert.Null(row.Error);
        Assert.Equal(expected, row.Nace);
    }

    [Theory]
    [InlineData("Evet", true)] [InlineData("EVET", true)] [InlineData("evet ", true)] [InlineData("Yes", true)] [InlineData("1", true)]
    [InlineData("Doğru", true)] [InlineData("Hayır", false)] [InlineData("", false)] [InlineData("hayir", false)]
    public void Herkese_acik_her_yazimla_okunur(string text, bool expected)
    {
        var row = SearchProfileTransfer.ParseRow(2, "A", "Makine", "", "", "", "", text);
        Assert.Null(row.Error);
        Assert.Equal(expected, row.IsPublic);
    }

    [Fact]
    public void Excel_mantiksal_DOGRU_hucresi_herkese_acik_sayilir()
    {
        var wb = Template(row: ("Mantıksal", "Makine", null));
        wb.Worksheet("Profiller").Cell(2, 8).Value = true;
        Assert.True(Assert.Single(Parse(wb).Rows).IsPublic);
    }

    [Theory]
    [InlineData("İTALYA", "IT", null)]
    [InlineData("almanya; fransa ; Almanya", "DE", "DE,FR")]
    [InlineData("de, FR, it", "DE", "DE,FR,IT")]
    [InlineData("Turkiye", "TR", null)]
    public void Ulkeler_buyuk_kucuk_harf_ve_kodla_okunur(string region, string first, string? keys)
    {
        var row = SearchProfileTransfer.ParseRow(2, "A", "Makine", "", region, "", "", "");
        Assert.Null(row.Error);
        Assert.Equal(first, row.RegionKey);
        Assert.Equal(keys, row.RegionKeys);
    }

    [Theory]
    [InlineData("Almanya, Fransa", "Bayern", "", "yalnızca tek ülke")]
    [InlineData("Almanya, Atlantis", "", "", "tanınmadı: Atlantis")]
    [InlineData("", "", "32.5", "tam sayı")]
    [InlineData("", "", "0", "tam sayı")]
    public void Hatali_profil_satiri_anlasilir_hata_verir(string region, string city, string max, string expected)
    {
        var row = SearchProfileTransfer.ParseRow(2, "A", "Makine", "", region, city, max, "");
        Assert.NotNull(row.Error);
        Assert.Contains(expected, row.Error);
    }

    [Fact]
    public void Otuzdan_fazla_ulke_reddedilir()
    {
        var all = string.Join(", ", EgebisLeadFinder.Data.SearchRegions.All.Take(31).Select(r => r.Key));
        Assert.Contains("en fazla", SearchProfileTransfer.ParseRow(2, "A", "Makine", "", all, "", "", "").Error);
    }

    [Theory]
    [InlineData("32", 32)] [InlineData("32,0", 32)] [InlineData("32.0", 32)]
    public void En_fazla_firma_yazilislari_okunur(string text, int expected)
    {
        Assert.Equal(expected, SearchProfileTransfer.ParseRow(2, "A", "Makine", "", "", "", text, "").MaxCompanies);
    }

    [Fact]
    public void Hata_degerli_hucre_bos_satir_ve_bolunmez_bosluk_sorun_cikarmaz()
    {
        var wb = Template(row: (" Boşluklu ad ", "Makine", null));
        var ws = wb.Worksheet("Profiller");
        ws.Cell(2, 7).FormulaA1 = "NA()"; // #N/A
        ws.Cell(4, 1).Value = "Üçüncü satır"; ws.Cell(4, 2).Value = "Kimya"; // arada boş satır

        var rows = Parse(wb).Rows;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Null(r.Error));
        Assert.Equal("Boşluklu ad", rows[0].Name);
        Assert.Null(rows[0].MaxCompanies);
        Assert.Equal(4, rows[1].LineNo);
    }

    [Fact]
    public void Profil_CSVsi_tum_ulkeleri_yazar_kisaltmaz()
    {
        var table = ExportService.SearchProfileTable(Profiles(), null);
        var multi = table.Rows[1];
        Assert.Equal("Almanya, Fransa, İtalya, İspanya", multi[Array.IndexOf(table.Headers, "Ülke / bölge")]);
        Assert.Null(multi[Array.IndexOf(table.Headers, "İl / bölge")]);
        Assert.All(table.Rows, r => Assert.Equal(table.Headers.Length, r.Length));
    }

    private static XLWorkbook Template((string Name, string Industry, string? Nace) row)
    {
        var wb = new XLWorkbook(new MemoryStream(SearchProfileTransfer.Build(Array.Empty<SearchProfile>(), template: true)));
        var ws = wb.Worksheet("Profiller");
        ws.Rows(2, 10).Clear();
        ws.Cell(2, 1).Value = row.Name; ws.Cell(2, 2).Value = row.Industry;
        if (row.Nace is not null) ws.Cell(2, 3).Value = row.Nace;
        return wb;
    }

    private static SearchProfileTransfer.ParseResult Parse(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return SearchProfileTransfer.Parse(ms);
    }

    // ---------------- Karşılaştırma, sektör, audit ----------------

    [Fact]
    public void Karsilastirma_ayni_ve_kirli_adli_firmalarla_olusur_sutunlar_kaymaz()
    {
        var comparison = new CompanyComparison
        {
            A = new Company { Name = "Aynı Ad" + Dirty }, B = new Company { Name = "Aynı Ad" + Dirty },
            Leader = CompareSide.A, LeaderReason = new string('x', 40_000)
        };
        comparison.Sections.Add(new CompareSection("Genel", null, new List<CompareRow>
        {
            new("Puan", "80", "60", CompareSide.A), new("Adres", Dirty, null), new("=Formül", "-5", "+3", CompareSide.B)
        }));
        comparison.Criteria.Add(Dirty);
        comparison.Ai = new CompareAiResult { Recommended = "B", Headline = Dirty, Summary = "Özet", AStrengths = { "Güçlü" }, NextSteps = { "Ara" } };

        var tables = CompanyComparisonService.ToExport(comparison);
        AssertWorkbook(tables);
    }

    [Fact]
    public void Sektor_analizi_sayilari_sayi_olarak_yazar()
    {
        var report = new SectorReport { Id = 3, Keyword = "makine", UserName = "Test", CreatedAt = DateTime.UtcNow };
        var result = new SectorAiResult
        {
            Overview = Dirty,
            Sectors =
            {
                new SectorAssessment { Code = "28.22", Name = "Kaldırma", FitScore = 80, Verdict = "uygun", Summary = Dirty, Reasons = { "a", "b" },
                    Stats = new SectorStats(12, 55, 70, 3, 4, 1) },
                new SectorAssessment { Code = "01.11", Name = "Tarım", FitScore = 10, Verdict = "uygun_degil" }
            }
        };

        var tables = SectorAnalysisService.ToExport(report, result);
        using var wb = AssertWorkbook(tables);
        var summary = wb.Worksheet(1);
        Assert.Equal(XLDataType.Number, summary.Cell(2, 3).DataType);
        Assert.Equal("01.11", summary.Cell(3, 1).GetString());
    }

    [Fact]
    public void Audit_disa_aktarimi_uzun_ve_kirli_kayitla_olusur()
    {
        var logs = new List<AuditLog>
        {
            new() { At = DateTime.UtcNow, UserName = "admin", Action = "Company.Export", Summary = Dirty, UserAgent = new string('u', 40_000), Ip = "::1", Success = true },
            new() { At = DateTime.UtcNow.AddHours(-1), UserName = "=user", Action = "Lead.Delete", Success = false, EntityType = "Lead", EntityId = "5" }
        };
        AssertWorkbook(new List<ExportTable>
        {
            AuditController.LogTable(logs), AuditController.SummaryTable(logs),
            AuditController.FilterTable(DateTime.Today, null, "admin", null, Dirty, false, 2, 2, "admin")
        });
    }

    // ---------------- Ortak Excel üreticisi ----------------

    [Fact]
    public void Ondalik_mantiksal_tarih_ve_bos_sayilar_dogru_turde_yazilir()
    {
        var table = new ExportTable("Türler", new[] { "Ondalık", "Mantıksal", "Gün", "NaN", "Uzun", "Decimal" },
            new List<object?[]> { new object?[] { 45.678, true, new DateOnly(2026, 11, 25), double.NaN, 5_000_000_000L, 12.345m } });
        using var wb = new XLWorkbook(new MemoryStream(ExportService.ToXlsx(new[] { table })));
        var ws = wb.Worksheet(1);
        Assert.Equal(45.68, ws.Cell(2, 1).GetDouble());
        Assert.Equal("Evet", ws.Cell(2, 2).GetString());
        Assert.Equal(new DateTime(2026, 11, 25), ws.Cell(2, 3).GetDateTime());
        Assert.True(ws.Cell(2, 4).IsEmpty());
        Assert.Equal(5_000_000_000d, ws.Cell(2, 5).GetDouble());
        Assert.Equal(12.35, ws.Cell(2, 6).GetDouble());

        var csv = System.Text.Encoding.UTF8.GetString(ExportService.ToCsv(table));
        Assert.Contains("45,68;Evet;25.11.2026;;5000000000;12,35", csv);
    }

    [Fact]
    public void Gecersiz_uzun_ve_ayni_sayfa_adlari_dosyayi_bozmaz()
    {
        var empty = new List<object?[]>();
        var tables = new[]
        {
            new ExportTable("Firmalar", new[] { "A" }, empty), new ExportTable("Firmalar", new[] { "A" }, empty),
            new ExportTable("Çok/uzun:sayfa*adı?[1] — otuz bir karakteri aşan", new[] { "A" }, empty), new ExportTable("", new[] { "A" }, empty)
        };
        using var wb = new XLWorkbook(new MemoryStream(ExportService.ToXlsx(tables)));
        Assert.Equal(4, wb.Worksheets.Count);
        Assert.All(wb.Worksheets, s => Assert.InRange(s.Name.Length, 1, 31));
        Assert.Equal(wb.Worksheets.Count, wb.Worksheets.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static XLWorkbook AssertWorkbook(List<ExportTable> tables)
    {
        foreach (var t in tables) Assert.All(t.Rows, r => Assert.Equal(t.Headers.Length, r.Length));
        var problems = new List<string>();
        ExportDataMatrixTests.CheckWorkbook(tables, "test", problems);
        Assert.Empty(problems);
        return new XLWorkbook(new MemoryStream(ExportService.ToXlsx(tables)));
    }
}
