using EgebisLeadFinder.Services.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>
/// Sistem Teshisi (yalnizca yonetici): sunucuya dogrudan erisim olmadan yavaslik/hata kaynagini
/// bulmak icin sunucu icinden olcumler, tarayici-sunucu hiz testi ve son loglar.
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize(Roles = nameof(EgebisLeadFinder.Models.UserRole.Admin))]
public class DiagnosticsController : Controller
{
    private readonly DiagnosticsService _diagnostics;

    public DiagnosticsController(DiagnosticsService diagnostics) => _diagnostics = diagnostics;

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Content(await _diagnostics.RunAsync(ct), "text/plain; charset=utf-8");
    }

    [HttpGet]
    public IActionResult Log(int lines = 300, bool problems = false)
    {
        Response.Headers.CacheControl = "no-store";
        return Content(_diagnostics.ReadLog(Math.Clamp(lines, 50, 3000), problems), "text/plain; charset=utf-8");
    }

    /// <summary>Tarayici hiz testi icin bos yanit: olculen sure neredeyse tamamen ag suresidir.</summary>
    [HttpGet]
    public IActionResult Ping()
    {
        Response.Headers.CacheControl = "no-store";
        return Content("ok", "text/plain");
    }
}
