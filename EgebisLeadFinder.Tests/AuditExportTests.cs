using ClosedXML.Excel;
using EgebisLeadFinder.Controllers;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

/// <summary>Faz-II madde 6: audit kayitlari Excel'e aktarilir.</summary>
public class AuditExportTests
{
    [Fact]
    public void Excel_kayit_ozet_ve_filtre_sayfalarini_icerir()
    {
        var rows = new List<AuditLog>
        {
            new() { UserName = "ali", Action = "login", At = DateTime.UtcNow.AddHours(-2) },
            new() { UserName = "ali", Action = "login.failed", Success = false, Summary = "=HYPERLINK(\"x\")" },
            new() { UserName = "ayse", Action = "login" }
        };

        var bytes = ExportService.ToXlsx(new[]
        {
            AuditController.LogTable(rows),
            AuditController.SummaryTable(rows),
            AuditController.FilterTable(null, null, "ali", null, null, false, total: 12000, exported: 10000, "admin")
        });

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(new[] { "Audit kayıtları", "Özet", "Filtre" }, wb.Worksheets.Select(w => w.Name));

        var log = wb.Worksheet("Audit kayıtları");
        Assert.Equal("Giriş", log.Cell(2, 3).GetString());
        // Formul gibi gorunen ozet metin olarak yazilir.
        Assert.False(log.Cell(3, 5).HasFormula);

        var summary = wb.Worksheet("Özet");
        Assert.Equal(4, summary.RowsUsed().Count()); // baslik + ali/login + ali/login.failed + ayse/login
        Assert.Contains(wb.Worksheet("Filtre").RowsUsed(), r => r.Cell(1).GetString() == "Not");
    }
}
