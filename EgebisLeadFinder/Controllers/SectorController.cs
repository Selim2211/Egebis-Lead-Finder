using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

/// <summary>NACE bazli sektor analizi: anahtar kelime -> NACE kodlari -> yapay zeka raporu.</summary>
public class SectorController : Controller
{
    private readonly SectorAnalysisService _sectors;

    public SectorController(SectorAnalysisService sectors) => _sectors = sectors;

    [HttpGet]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var model = new SectorIndexViewModel
        {
            Keyword = q?.Trim(),
            Reports = await _sectors.Visible(User.UserId(), User.IsAdmin())
                .OrderByDescending(r => r.CreatedAt).Take(30).ToListAsync(ct)
        };

        if (!string.IsNullOrWhiteSpace(model.Keyword))
        {
            if (model.Keyword.Length > 150) model.Keyword = model.Keyword[..150];
            (model.Matches, model.AiError) = await _sectors.MatchAsync(model.Keyword, ct);
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(string keyword, List<string>? codes, CancellationToken ct)
    {
        keyword = (keyword ?? string.Empty).Trim();
        var selection = SectorAnalysisService.ParseSelection(codes);
        var (report, error) = await _sectors.AnalyzeAsync(keyword, selection, User.UserId(), User.DisplayName(), ct);

        if (report is null)
        {
            TempData["SettingsError"] = error;
            return RedirectToAction(nameof(Index), new { q = keyword });
        }

        AuditActionFilter.SetAuditSummary(HttpContext, $"Sektör analizi: {keyword} ({report.Codes})");
        return RedirectToAction(nameof(Report), new { id = report.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Report(int id, CancellationToken ct)
    {
        var report = await _sectors.Visible(User.UserId(), User.IsAdmin()).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report is null) return NotFound();
        return View(new SectorReportViewModel { Report = report, Result = SectorAnalysisService.Parse(report) });
    }

    [HttpGet]
    public async Task<IActionResult> Export(int id, CancellationToken ct)
    {
        var report = await _sectors.Visible(User.UserId(), User.IsAdmin()).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report is null) return NotFound();

        var tables = SectorAnalysisService.ToExport(report, SectorAnalysisService.Parse(report));
        return File(ExportService.ToXlsx(tables), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"sektor-analizi-{report.Id}-{report.CreatedAt.ToLocalTime():yyyy-MM-dd}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, [FromServices] EgebisLeadFinder.Data.ApplicationDbContext db, CancellationToken ct)
    {
        var visible = await _sectors.Visible(User.UserId(), User.IsAdmin()).AnyAsync(r => r.Id == id, ct);
        if (!visible) return NotFound();

        await db.SectorReports.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
        TempData["SettingsSaved"] = "Rapor silindi.";
        return RedirectToAction(nameof(Index));
    }
}
