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

    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Gorunen profilleri (ids verilirse yalnizca secilenleri) indirir. Excel, "İçe aktar" ile geri
    /// yuklenebilen bicimdedir (aciklama sayfasiyla); CSV yalnizca okumak icindir.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Export(string? format, int[]? ids, CancellationToken ct)
    {
        var profiles = await VisibleAsync(ct);
        if (ids is { Length: > 0 }) profiles = profiles.Where(p => ids.Contains(p.Id)).ToList();

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        AuditActionFilter.SetAuditSummary(HttpContext, $"Arama profilleri dışa aktarıldı ({profiles.Count} profil)");
        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return File(ExportService.ToCsv(ExportService.SearchProfileTable(profiles, User.UserId())), "text/csv; charset=utf-8", $"arama-profilleri-{stamp}.csv");
        return File(SearchProfileTransfer.Build(profiles, template: false), XlsxType, $"arama-profilleri-{stamp}.xlsx");
    }

    /// <summary>Ice aktarma sablonu: ornek satirlar ve alan aciklamalari.</summary>
    [HttpGet]
    public IActionResult Template() =>
        File(SearchProfileTransfer.Build(Array.Empty<SearchProfile>(), template: true), XlsxType, "arama-profili-sablonu.xlsx");

    /// <summary>Excel'deki profilleri ice aktarir. Hatali satirlar atlanir, ayni adli profil varsa o satir atlanir.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ProfilesError"] = "İçe aktarmak için bir Excel (.xlsx) dosyası seçin.";
            return RedirectToAction(nameof(Index));
        }
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || file.Length > SearchProfileTransfer.MaxFileBytes)
        {
            TempData["ProfilesError"] = "Yalnızca 1 MB'a kadar .xlsx dosyası yüklenebilir. Şablonu indirip onu doldurun.";
            return RedirectToAction(nameof(Index));
        }

        SearchProfileTransfer.ParseResult parsed;
        await using (var stream = file.OpenReadStream())
            parsed = SearchProfileTransfer.Parse(stream);
        if (parsed.FileError is not null && parsed.Rows.Count == 0)
        {
            TempData["ProfilesError"] = parsed.FileError;
            return RedirectToAction(nameof(Index));
        }

        var me = User.UserId();
        var mine = await _db.SearchProfiles.Where(p => p.OwnerUserId == me).Select(p => p.Name).ToListAsync(ct);
        var names = new HashSet<string>(mine, StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var problems = new List<string>();
        foreach (var row in parsed.Rows)
        {
            if (row.Error is not null) { problems.Add($"Satır {row.LineNo}: {row.Error}"); continue; }
            if (!names.Add(row.Name!)) { problems.Add($"Satır {row.LineNo}: \"{row.Name}\" adlı profiliniz zaten var, atlandı."); continue; }

            _db.SearchProfiles.Add(new SearchProfile
            {
                Name = row.Name!,
                Industry = row.Industry,
                NaceCode = row.Nace,
                RegionKey = row.RegionKey,
                Country = row.Country,
                City = row.City,
                MaxCompanies = row.MaxCompanies,
                IsPublic = row.IsPublic,
                OwnerUserId = me
            });
            created++;
        }
        if (created > 0) await _db.SaveChangesAsync(ct);

        AuditActionFilter.SetAuditSummary(HttpContext, $"Arama profili içe aktarıldı: {created} eklendi, {problems.Count} satır atlandı");
        TempData["ProfilesOk"] = created > 0 ? $"{created} arama profili eklendi." : null;
        var notes = parsed.FileError is null ? problems : problems.Append(parsed.FileError).ToList();
        if (notes.Count > 0)
            TempData[created > 0 ? "ProfilesNotes" : "ProfilesError"] = created > 0
                ? string.Join("\n", notes.Take(10))
                : "Hiçbir profil eklenmedi. " + string.Join(" ", notes.Take(5));
        return RedirectToAction(nameof(Index));
    }
}
