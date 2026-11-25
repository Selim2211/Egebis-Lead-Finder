using ClosedXML.Excel;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// "Toplu Excel'e aktar": seçilen her firma tek dosyada kendi sayfasında; sayfadaki bilgi tek firma
/// "Excel'e aktar" ile birebir aynı (Firma / Kişiler / Leadler bölümleri alt alta).
/// </summary>
public class BulkCompanyExportTests
{
    [Fact]
    public void Her_firma_ayri_sayfada_ve_tek_firma_aktarimiyla_ayni_bilgide()
    {
        var companies = ExportDataMatrixTests.Stored();
        using var wb = new XLWorkbook(new MemoryStream(ExportService.ToXlsxCompanies(companies)));

        Assert.Equal(companies.Count + 1, wb.Worksheets.Count);
        Assert.Equal("Firmalar", wb.Worksheet(1).Name);

        for (var i = 0; i < companies.Count; i++)
        {
            var company = companies[i];
            var sheet = wb.Worksheet(i + 2);
            Assert.Equal(ExportService.Clean(company.Name), sheet.Cell(1, 1).GetString());

            // Bolumleri sirayla bul ve her hucreyi tek firma tablolariyla karsilastir.
            var row = 3;
            foreach (var table in ExportService.CompanyDetailTables(company))
            {
                Assert.Equal(table.Name, sheet.Cell(row, 1).GetString());
                row++;
                for (var c = 0; c < table.Headers.Length; c++)
                    Assert.Equal(table.Headers[c], sheet.Cell(row, c + 1).GetString());
                Assert.True(sheet.Cell(row, table.Headers.Length + 1).IsEmpty(), $"{company.Name}/{table.Name}: başlık taşıyor");
                row++;
                foreach (var values in table.Rows)
                {
                    for (var c = 0; c < table.Headers.Length; c++)
                    {
                        var cell = sheet.Cell(row, c + 1);
                        var got = cell.DataType == XLDataType.DateTime ? cell.GetDateTime().ToString("dd.MM.yyyy HH:mm") : cell.GetString();
                        Assert.False(cell.HasFormula);
                        Assert.True(Same(values[c], got), $"{company.Name}/{table.Name} satır {row} '{table.Headers[c]}': '{got}'");
                    }
                    Assert.True(sheet.Cell(row, table.Headers.Length + 1).IsEmpty(), $"{company.Name}/{table.Name} satır {row}: sütun taşıyor");
                    row++;
                }
                if (table.Rows.Count == 0) { Assert.Equal("Kayıt yok", sheet.Cell(row, 1).GetString()); row++; }
                row++; // bos satir
            }
        }
    }

    [Fact]
    public void Liste_sayfasi_her_firmayi_kendi_sayfasina_baglar()
    {
        var companies = ExportDataMatrixTests.Stored();
        using var wb = new XLWorkbook(new MemoryStream(ExportService.ToXlsxCompanies(companies)));
        var index = wb.Worksheet(1);

        for (var i = 0; i < companies.Count; i++)
        {
            var cell = index.Cell(i + 2, 1);
            Assert.Equal(ExportService.Clean(companies[i].Name), cell.GetString());
            Assert.True(cell.HasHyperlink);
            var target = wb.Worksheet(i + 2).Name;
            Assert.Contains(target.Replace("'", "''"), cell.GetHyperlink().InternalAddress);
            Assert.Equal(companies[i].Contacts.Count, index.Cell(i + 2, 8).GetValue<int>());
            Assert.Equal(companies[i].Leads.Count, index.Cell(i + 2, 9).GetValue<int>());
        }
    }

    [Fact]
    public void Ayni_adli_uzun_adli_ve_gecersiz_karakterli_firmalar_ayri_sayfa_alir()
    {
        var companies = new List<Company>
        {
            new() { Id = 1, Name = "Firmalar" },
            new() { Id = 2, Name = "Ege Makina" },
            new() { Id = 3, Name = "EGE MAKINA" },
            new() { Id = 4, Name = "Çok/Uzun:Adlı*Firma?[Sanayi] ve Ticaret Anonim Şirketi Holding" },
            new() { Id = 5, Name = "'Tırnaklı' Firma" },
            new() { Id = 6, Name = "" }
        };
        using var wb = new XLWorkbook(new MemoryStream(ExportService.ToXlsxCompanies(companies)));

        Assert.Equal(7, wb.Worksheets.Count);
        Assert.Equal(7, wb.Worksheets.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(wb.Worksheets, s => Assert.InRange(s.Name.Length, 1, 31));
        // Her baglanti dogru sayfayi acar.
        for (var i = 0; i < companies.Count; i++)
        {
            var address = wb.Worksheet(1).Cell(i + 2, 1).GetHyperlink().InternalAddress;
            var sheetName = address[..address.LastIndexOf('!')].Trim('\'').Replace("''", "'");
            Assert.Equal(wb.Worksheet(i + 2).Name, sheetName);
        }
    }

    private static bool Same(object? expected, string got)
    {
        var want = expected switch
        {
            null => "",
            DateTime d => d.ToString("dd.MM.yyyy HH:mm"),
            int n => n.ToString(),
            _ => ExportService.Clean(expected.ToString())
        };
        return want.Replace("\r\n", "\n") == got.Replace("\r\n", "\n");
    }
}
