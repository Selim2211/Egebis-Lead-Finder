using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>Favori firmalar: kullanicinin kendi listesi ve baskalarinin herkese acik listeleri.</summary>
public class FavoritesController : Controller
{
    private readonly FavoriteService _favorites;

    public FavoritesController(FavoriteService favorites) => _favorites = favorites;

    [HttpGet]
    public async Task<IActionResult> Index(int? user, CancellationToken ct)
    {
        var me = User.UserId();
        var ownerId = user ?? me;
        if (ownerId is null) return Challenge();

        // Baskasinin listesi yalnizca herkese aciksa gorunur.
        if (!await _favorites.CanViewAsync(me, ownerId.Value, ct))
        {
            TempData["SettingsError"] = "Bu favori listesi paylaşılmamış.";
            return RedirectToAction(nameof(Index), new { user = (int?)null });
        }

        var lists = await _favorites.ListsAsync(me, ct);
        var model = new FavoritesViewModel
        {
            Lists = lists,
            Selected = lists.FirstOrDefault(l => l.UserId == ownerId),
            IsOwn = ownerId == me,
            Entries = await _favorites.EntriesAsync(ownerId.Value, ct)
        };
        // Baskasinin listesindeki firmalar icin kendi yildizlarim.
        model.MyFavoriteIds = model.IsOwn
            ? model.Entries.Select(e => e.CompanyId).ToHashSet()
            : await _favorites.FavoriteIdsAsync(me, model.Entries.Select(e => e.CompanyId), ct);
        return View(model);
    }

    /// <summary>Yildiz: favoriye ekle / cikar. JS acikken JSON doner, kapaliysa geri yonlendirir.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, string? returnUrl, CancellationToken ct)
    {
        var me = User.UserId();
        if (me is null) return Challenge();

        var state = await _favorites.ToggleAsync(me.Value, id, User.IsAdmin(), ct);
        if (state is null) return NotFound();

        if (Request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true))
            return Json(new { favorite = state.Value });

        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction(nameof(Index));
    }

    /// <summary>Listeyi herkese acar veya kisiye ozel yapar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Visibility(bool isPublic, CancellationToken ct)
    {
        var me = User.UserId();
        if (me is null) return Challenge();

        await _favorites.SetPublicAsync(me.Value, isPublic, ct);
        AuditActionFilter.SetAuditSummary(HttpContext, isPublic ? "Favori listesi herkese açıldı" : "Favori listesi kişiye özel yapıldı");
        TempData["SettingsSaved"] = isPublic
            ? "Favori listeniz artık tüm kullanıcılara açık (yalnızca görüntüleyebilirler)."
            : "Favori listeniz artık yalnızca size özel.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Listeyi Excel olarak indirir (firmalar + kisiler sayfasi).</summary>
    [HttpGet]
    public async Task<IActionResult> Export(int? user, CancellationToken ct)
    {
        var me = User.UserId();
        var ownerId = user ?? me;
        if (ownerId is null) return Challenge();
        if (!await _favorites.CanViewAsync(me, ownerId.Value, ct)) return NotFound();

        var companies = (await _favorites.EntriesAsync(ownerId.Value, ct)).Select(e => e.Company).ToList();
        var tables = ExportService.CompanyTables(companies);
        return File(ExportService.ToXlsx(tables),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"favoriler-{DateTime.Now:yyyy-MM-dd-HHmm}.xlsx");
    }
}
