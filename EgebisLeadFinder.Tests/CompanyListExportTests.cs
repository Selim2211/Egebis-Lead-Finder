using System.Text;
using ClosedXML.Excel;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Firmalar listesi "Dışa aktar": web sitesinden gelen bozuk karakterler, çok uzun metin, ";" / tırnak / satır sonu
/// içeren değerler olsa da Excel dosyası oluşur ve her değer kendi sütununda kalır (CSV dahil).
/// </summary>
public class CompanyListExportTests
{
    private const string Dirty = "Satır1\nSatır2; \"tırnak\" \u000B dikey sekme \u0001\u001F kontrol \uD800 yalnız vekil ✓ 🚀";

    private static List<Company> Companies()
    {
        var clean = new Company
        {
            Id = 1, Name = "Ege Makina", Website = "https://ege.com", Address = "OSB 1. Cad. No:5", City = "Manisa", Country = "Türkiye",
            Industry = "Makine", NaceCode = "28.22.01", Score = 80, FitScore = 70, FitReason = "Üretici",
            CreatedAt = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc)
        };
        var dirty = new Company
        {
            Id = 2, Name = "=HYPERLINK(\"x\")", Website = "-ege", Address = Dirty, City = "İzmir", Country = "Türkiye",
            Industry = Dirty, NaceCode = "22.19", Score = 60, FitScore = null, FitReason = new string('a', 40_000),
            AiAnalysis = "{\"industry\":\"Plastik\\u000b\",\"reason\":\"" + new string('b', 50) + "\"}",
            CreatedAt = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc)
        };
        dirty.Contacts.Add(new Contact { Id = 5, Name = "Ali\u0002 Veli", Title = "IT; Müdür", Email = "ali@ege.com", Phone = "+90 555" });
        var third = new Company { Id = 3, Name = "Son Firma", City = "Bursa", Country = "Türkiye", Score = 50, CreatedAt = DateTime.UtcNow };
        return new() { clean, dirty, third };
    }

    [Fact]
    public void Excel_bozuk_karakter_ve_uzun_metinle_de_olusur_ve_sutunlar_kaymaz()
    {
        var tables = ExportService.CompanyTables(Companies());
        var bytes = ExportService.ToXlsx(tables);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var sheet = wb.Worksheet(1);
        var headers = tables[0].Headers;
        int Col(string h) => Array.IndexOf(headers, h) + 1;

        Assert.Equal(4, sheet.LastRowUsed()!.RowNumber());
        Assert.True(sheet.LastColumnUsed()!.ColumnNumber() <= headers.Length);
        Assert.Equal("Ege Makina", sheet.Cell(2, Col("Firma")).GetString());
        Assert.Equal("=HYPERLINK(\"x\")", sheet.Cell(3, Col("Firma")).GetString());
        Assert.False(sheet.Cell(3, Col("Firma")).HasFormula);
        Assert.Equal("İzmir", sheet.Cell(3, Col("Şehir")).GetString());
        Assert.Equal("22.19", sheet.Cell(3, Col("NACE")).GetString());
        Assert.Equal(60, sheet.Cell(3, Col("Puan")).GetValue<int>());
        Assert.Equal("Son Firma", sheet.Cell(4, Col("Firma")).GetString());
        Assert.Equal("Bursa", sheet.Cell(4, Col("Şehir")).GetString());
        Assert.True(sheet.Cell(3, Col("Neden")).GetString().Length <= 32_767);
        Assert.Contains("Satır2", sheet.Cell(3, Col("Adres")).GetString());

        var people = wb.Worksheet(2);
        Assert.Equal("=HYPERLINK(\"x\")", people.Cell(2, 1).GetString());
        Assert.Equal("IT; Müdür", people.Cell(2, 3).GetString());
        Assert.Equal("ali@ege.com", people.Cell(2, 4).GetString());
    }

    [Fact]
    public void CSV_her_kayitta_baslik_kadar_alan_tasir()
    {
        var table = ExportService.CompanyTables(Companies())[0];
        var text = Encoding.UTF8.GetString(ExportService.ToCsv(table)).TrimStart('﻿');
        var records = ParseCsv(text, ';');

        Assert.Equal(4, records.Count);
        Assert.All(records, r => Assert.Equal(table.Headers.Length, r.Count));
        Assert.Equal("Ege Makina", records[1][0]);
        Assert.Equal("İzmir", records[2][Array.IndexOf(table.Headers, "Şehir")]);
        Assert.Equal("Son Firma", records[3][0]);
        Assert.DoesNotContain(text, ch => ch < ' ' && ch is not ('\r' or '\n'));
    }

    /// <summary>RFC 4180 ayrıştırıcı (Excel'in yaptığı gibi): tırnak içindeki ayraç ve satır sonu alanı bölmez.</summary>
    private static List<List<string>> ParseCsv(string s, char sep)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < s.Length && s[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == sep) { row.Add(field.ToString()); field.Clear(); }
            else if (ch == '\r') { }
            else if (ch == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new(); }
            else field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
