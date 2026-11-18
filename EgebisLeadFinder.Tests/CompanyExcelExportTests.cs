using ClosedXML.Excel;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

/// <summary>Firma sayfasindaki "Excel'e aktar": tum bilgi gelir, sutunlar kaymaz.</summary>
public class CompanyExcelExportTests
{
    private static Company Sample()
    {
        var c = new Company
        {
            Id = 7, Name = "Haustek", Website = "https://haustek.com", Domain = "haustek.com", Industry = "Beyaz Eşya",
            NaceCode = "27.51.08", City = "Manisa", Country = "Türkiye", Address = "OSB 1. Cad.", Score = 76, FitScore = 85,
            FitSegment = "Üretici", FitReason = "Güçlü aday", Description = "Ankastre ürünler",
            AiAnalysis = "{\"industry\":\"Beyaz Eşya\",\"manufacturer\":true,\"products\":[\"Ocak\",\"Fırın\"],\"sap\":\"likely\",\"potential\":true,\"reason\":\"Üretici\"}",
            RatingJson = "{\"signal\":\"guclu\",\"summary\":\"Köklü firma\",\"customers\":[\"A\",\"B\"],\"riskSignals\":[{\"text\":\"Dava\",\"severity\":\"orta\"}]}",
            RatingSignal = "Guclu", CreatedAt = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc)
        };
        var ali = new Contact { Id = 1, Name = "Ali Veli", Title = "Genel Müdür", Email = "ali@haustek.com", Phone = "0555 111", Location = "Manisa", TitleScore = 90 };
        var ayse = new Contact { Id = 2, Name = "Ayşe Kaya", Title = "IT Müdürü", Email = null, TitleScore = 80 };
        c.Contacts.Add(ali); c.Contacts.Add(ayse);
        c.Leads.Add(new Lead { Id = 1, ContactId = 1, Score = 76, Status = LeadStatus.Gonderildi, Notes = "Salı ara" });
        return c;
    }

    [Fact]
    public void Her_sayfada_her_satir_baslik_kadar_hucre_tasir_kayma_olmaz()
    {
        var tables = ExportService.CompanyDetailTables(Sample());

        Assert.Equal(3, tables.Count);
        foreach (var t in tables)
            foreach (var row in t.Rows)
                Assert.Equal(t.Headers.Length, row.Length);
    }

    [Fact]
    public void Excel_dosyasinda_firma_kisi_ve_lead_dogru_sutunda()
    {
        var bytes = ExportService.ToXlsx(ExportService.CompanyDetailTables(Sample()));
        using var wb = new XLWorkbook(new MemoryStream(bytes));

        var firm = wb.Worksheet(1);
        string Value(string field) => firm.Rows().Skip(1).First(r => r.Cell(1).GetString() == field).Cell(2).GetString();
        Assert.Equal("Haustek", Value("Firma adı"));
        Assert.Equal("27.51.08", Value("NACE kodu"));
        Assert.Equal("Manisa", Value("Şehir"));
        Assert.Equal("76", Value("Lead puanı"));
        Assert.Equal("Ocak; Fırın", Value("Yapay zekâ: ürünler"));
        Assert.Equal("A; B", Value("Ön araştırma: müşteriler"));
        Assert.Equal("Dava (orta)", Value("Ön araştırma: risk işaretleri"));
        Assert.Equal("Güçlü", Value("Ön araştırma: sinyal"));

        var people = wb.Worksheet(2);
        Assert.Equal("Ad soyad", people.Cell(1, 1).GetString());
        Assert.Equal("Ali Veli", people.Cell(2, 1).GetString());
        Assert.Equal("Genel Müdür", people.Cell(2, 2).GetString());
        Assert.Equal("ali@haustek.com", people.Cell(2, 3).GetString());
        Assert.Equal("0555 111", people.Cell(2, 5).GetString());
        Assert.Equal("Evet", people.Cell(2, 11).GetString());
        Assert.Equal("Ayşe Kaya", people.Cell(3, 1).GetString());
        Assert.Equal("", people.Cell(3, 3).GetString());        // e-postasi yok: bos hucre, kayma yok
        Assert.Equal("Hayır", people.Cell(3, 11).GetString());
        Assert.Equal(3, people.LastRowUsed()!.RowNumber());
        Assert.Equal(11, people.LastColumnUsed()!.ColumnNumber());

        var leads = wb.Worksheet(3);
        Assert.Equal("Ali Veli", leads.Cell(2, 1).GetString());
        Assert.Equal("Mail atıldı", leads.Cell(2, 4).GetString());
        Assert.Equal("Salı ara", leads.Cell(2, 10).GetString());
    }

    [Fact]
    public void Kisi_ve_lead_yoksa_basliklar_yine_yazilir()
    {
        var c = Sample(); c.Contacts.Clear(); c.Leads.Clear();
        using var wb = new XLWorkbook(new MemoryStream(ExportService.ToXlsx(ExportService.CompanyDetailTables(c))));
        Assert.Equal("Ad soyad", wb.Worksheet(2).Cell(1, 1).GetString());
        Assert.Equal("Kişi", wb.Worksheet(3).Cell(1, 1).GetString());
    }
}
