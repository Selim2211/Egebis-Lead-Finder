using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>
/// Black List (yonetici): aramada bulunsa dahi gosterilmeyecek alan adlari / firma adlari.
/// Firma kartindaki "Blackliste ekle" buraya ekler. Liste Ayarlar'daki Search:ExtraBlockedDomains kaydinda tutulur.
/// </summary>
[Authorize(Roles = nameof(UserRole.Admin))]
public class BlacklistController : Controller
{
    private readonly ISettingsService _settings;

    public BlacklistController(ISettingsService settings) => _settings = settings;

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? type, string? sort, int page = 1, CancellationToken ct = default)
    {
        var all = SearchBlacklist.Entries(await _settings.GetAsync(SettingKeys.ExtraBlockedDomains, ct));
        var (items, total, current) = BlacklistListing.Query(all, q, type, sort, page);
        return View(new BlacklistViewModel
        {
            Items = items, Q = q, Type = type, Sort = string.IsNullOrWhiteSpace(sort) ? BlacklistListing.Newest : sort,
            AllCount = all.Count,
            Pager = new PagerModel { Page = current, PageSize = BlacklistListing.PageSize, TotalItems = total }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(string? entry, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            TempData["SettingsError"] = "Black List'e eklemek için bir alan adı veya firma adı yazın.";
            return RedirectToAction(nameof(Index));
        }

        var updated = SearchBlacklist.Add(await _settings.GetAsync(SettingKeys.ExtraBlockedDomains, ct), entry);
        await _settings.SetManyAsync(new Dictionary<string, string?> { [SettingKeys.ExtraBlockedDomains] = updated }, ct);
        AuditActionFilter.SetAuditSummary(HttpContext, $"Black List'e eklendi: {entry.Trim()}");
        TempData["SettingsSaved"] = "Black List'e eklendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(string? entry, string? returnUrl, CancellationToken ct)
    {
        var updated = SearchBlacklist.Remove(await _settings.GetAsync(SettingKeys.ExtraBlockedDomains, ct), entry);
        await _settings.SetManyAsync(new Dictionary<string, string?> { [SettingKeys.ExtraBlockedDomains] = updated.Length == 0 ? null : updated }, ct);
        AuditActionFilter.SetAuditSummary(HttpContext, $"Black List'ten çıkarıldı: {entry?.Trim()}");
        TempData["SettingsSaved"] = "Black List'ten çıkarıldı.";
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction(nameof(Index));
    }
}
