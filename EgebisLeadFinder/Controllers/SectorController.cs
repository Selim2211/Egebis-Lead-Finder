using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Auth;
using EgebisLeadFinder.Services.Progress;
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

    /// <summary>
    /// Analizi arka planda baslatir (ilerleme cubugu + iptal). Yapay zeka cagrisi uzun surebildigi icin
    /// istek icinde beklenmez; JavaScript kapaliysa form yukaridaki Analyze'a duser.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult StartAnalyze(string keyword, List<string>? codes, [FromServices] JobProgressStore progress,
        [FromServices] IServiceScopeFactory scopes, [FromServices] ILogger<SectorController> logger)
    {
        keyword = (keyword ?? string.Empty).Trim();
        var selection = SectorAnalysisService.ParseSelection(codes);
        if (SectorAnalysisService.ValidateSelection(selection) is { } invalid) return BadRequest(new { error = invalid });

        var job = progress.Create();
        var userId = User.UserId();
        var userName = User.DisplayName();
        var reportBase = Url.Action(nameof(Report))!;
        AuditActionFilter.SetAuditSummary(HttpContext, $"Sektör analizi başlatıldı: {keyword} ({string.Join(",", selection.Select(s => s.Code))})");

        _ = Task.Run(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var sectors = scope.ServiceProvider.GetRequiredService<SectorAnalysisService>();
            try
            {
                var (report, error) = await sectors.AnalyzeAsync(keyword, selection, userId, userName, job.Token,
                    (pct, stage) => progress.Report(job.Id, pct, stage));
                if (report is null) progress.Fail(job.Id, error ?? "Sektör analizi tamamlanamadı.");
                else progress.Complete(job.Id, $"{reportBase}/{report.Id}");
            }
            catch (OperationCanceledException) when (job.CancelRequested)
            {
                progress.MarkCancelled(job.Id, null, detail: "Analiz iptal edildi; rapor kaydedilmedi.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sektör analizi başarısız (arka plan).");
                progress.Fail(job.Id, ex.Message);
            }
        });

        return Json(new { jobId = job.Id, progressUrl = Url.Action("JobStatus", "Company", new { jobId = job.Id }) });
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
