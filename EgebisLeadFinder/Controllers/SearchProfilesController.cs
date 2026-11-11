using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

/// <summary>
/// Arama Profilleri: listeleme, silme, disa aktarma ve kisiye ozel varsayilan profil secimi.
/// Olusturma ve duzenleme ortak Arama Profili penceresinden CompanyController.SaveProfile ile yapilir.
/// </summary>
public class SearchProfilesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ISettingsService _settings;

    public SearchProfilesController(ApplicationDbContext db, ISettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    private Task<List<SearchProfile>> VisibleAsync(CancellationToken ct)
    {
        var me = User.UserId();
        return _db.SearchProfiles.AsNoTracking().VisibleTo(me).Include(p => p.Owner)
            .OrderByDescending(p => p.OwnerUserId == me).ThenBy(p => p.Name).ToListAsync(ct);
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var me = User.UserId();
        var defaultId = await _db.Users.AsNoTracking().Where(u => u.Id == me).Select(u => u.DefaultSearchProfileId).FirstOrDefaultAsync(ct);
        var max = int.TryParse(await _settings.GetAsync(SettingKeys.SearchMaxCompanies, ct), out var m) && m > 0
            ? Math.Min(m, SettingKeys.SearchMaxCompaniesUpperLimit)
            : SettingKeys.DefaultSearchMaxCompanies;
        return View(new SearchProfilesViewModel
        {
            Profiles = await VisibleAsync(ct),
            DefaultProfileId = defaultId,
            DefaultMaxCompanies = max,
            Region = await CompanyController.GetRegionAsync(_settings, ct)
        });
    }

    /// <summary>Varsayilan profili secer; id bossa kaldirir. Firma adiyla arama bu profili kullanir.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(int? id, CancellationToken ct)
    {
        var me = User.UserId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == me, ct);
        if (user is null) return Forbid();

        SearchProfile? profile = null;
        if (id is not null)
        {
            profile = await _db.SearchProfiles.AsNoTracking().VisibleTo(me).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (profile is null) return NotFound();
        }

        user.DefaultSearchProfileId = profile?.Id;
        await _db.SaveChangesAsync(ct);
        AuditActionFilter.SetAuditSummary(HttpContext, profile is null ? "Varsayılan arama profili kaldırıldı" : $"Varsayılan arama profili: {profile.Name}");
        TempData["ProfilesOk"] = profile is null ? "Varsayılan arama profili kaldırıldı." : $"\"{profile.Name}\" varsayılan arama profili yapıldı.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var me = User.UserId();
        var profile = await _db.SearchProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null || !profile.IsVisibleTo(me)) return NotFound();
        if (!profile.CanEdit(me, User.IsAdmin()))
        {
            TempData["ProfilesError"] = "Bu profil başka bir kullanıcıya ait; yalnızca sahibi silebilir.";
            return RedirectToAction(nameof(Index));
        }

        _db.SearchProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);
        AuditActionFilter.SetAuditSummary(HttpContext, $"Arama profili silindi: {profile.Name}");
        TempData["ProfilesOk"] = $"\"{profile.Name}\" profili silindi (firmalar silinmedi).";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Gorunen profilleri Excel veya CSV olarak indirir.</summary>
    [HttpGet]
    public async Task<IActionResult> Export(string? format, CancellationToken ct)
    {
        var table = ExportService.SearchProfileTable(await VisibleAsync(ct), User.UserId());
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? File(ExportService.ToCsv(table), "text/csv; charset=utf-8", $"arama-profilleri-{stamp}.csv")
            : File(ExportService.ToXlsx(new[] { table }), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"arama-profilleri-{stamp}.xlsx");
    }
}
