using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

/// <summary>
/// API Kullanimi: hangi anahtar ne kadar kredi/cagri kullandi, hangisinin kotasi dolmak uzere.
/// Tum kullanicilar gorebilir; limitler ve anahtarlar Ayarlar'dan (yonetici) degisir.
/// </summary>
public class UsageController : Controller
{
    private readonly UsageService _usage;

    public UsageController(UsageService usage) => _usage = usage;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await _usage.GetAllAsync(ct));

    /// <summary>
    /// Kullanilan Krediler (yonetici): secilen gun/hafta/ay icin gun gun Serper, Apollo ve Gemini harcamasi, toplam ve o donemdeki
    /// aramalarin tek tek maliyeti. date: donemin icinde herhangi bir gun (yyyy-MM-dd); bossa bugun.
    /// </summary>
    [HttpGet]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Credits(string? period, string? date, [FromServices] Data.ApplicationDbContext db, CancellationToken ct)
    {
        var today = UsageService.Today;
        var anchor = DateOnly.TryParseExact(date, "yyyy-MM-dd", out var d) ? d : today;
        // Gecersiz uc degerler (0001-01-01, 9999-12-31) donem hesabini bozmasin.
        if (anchor < new DateOnly(2020, 1, 1)) anchor = new DateOnly(2020, 1, 1);
        if (anchor > today.AddYears(1)) anchor = today;
        var resolved = CreditsPeriod.Resolve(period, anchor);
        var report = await _usage.GetCreditsAsync(resolved, ct);

        // Donemdeki aramalar (Turkiye gunu = UTC+3): kim, ne aradi, kac kredi harcadi.
        var (fromUtc, toUtc) = resolved.UtcRange();
        var runs = await db.SearchRuns.AsNoTracking().Include(r => r.User)
            .Where(r => r.StartedAt >= fromUtc && r.StartedAt < toUtc)
            .OrderByDescending(r => r.StartedAt).Take(200).ToListAsync(ct);

        Response.Headers.CacheControl = "no-store";
        return View(new CreditsViewModel { Report = report, Runs = runs });
    }

    /// <summary>Acilis uyarisi penceresi (layout sayfa acildiktan sonra yukler; bos yanit = sorun yok).</summary>
    [HttpGet]
    public IActionResult StartupAlert()
    {
        Response.Headers.CacheControl = "no-store";
        return PartialView("_ApiStatusAlert");
    }

    /// <summary>Apollo'nun canli kredi durumunu sorgular (bakiye API'si olan tek saglayici).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var status = await _usage.RefreshApolloAsync(ct);
        TempData[status.Ok ? "SettingsSaved" : "SettingsError"] = status.Ok
            ? $"Apollo kredi durumu güncellendi: {status.LeftOver} kredi kaldı."
            : $"Apollo kredi durumu alınamadı: {status.Error} Yerel sayaç gösteriliyor.";
        return RedirectToAction(nameof(Index));
    }
}
