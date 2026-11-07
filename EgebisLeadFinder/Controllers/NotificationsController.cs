using EgebisLeadFinder.Services.Auth;
using EgebisLeadFinder.Services.Inbox;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>Sag ustteki zil: liste (acilinca yuklenir), sayilar (periyodik), bildirimi acma, tumunu okundu yapma.</summary>
public class NotificationsController : Controller
{
    private readonly NotificationService _notifications;

    public NotificationsController(NotificationService notifications) => _notifications = notifications;

    [HttpGet]
    public async Task<IActionResult> Panel(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return PartialView("_NotificationPanel", await _notifications.LatestAsync(User.UserId() ?? 0, 12, ct));
    }

    /// <summary>Zil ve Mesajlar rozetleri (sayfa acikken dakikada bir yenilenir).</summary>
    [HttpGet]
    public async Task<IActionResult> Count([FromServices] InboxService inbox, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Json(new
        {
            notifications = await _notifications.UnreadCountAsync(User.UserId() ?? 0, ct),
            messages = await inbox.UnreadRepliesAsync(ct)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Open(int id, CancellationToken ct)
    {
        var leadId = await _notifications.OpenAsync(User.UserId() ?? 0, id, ct);
        return leadId is int lead
            ? RedirectToAction("Index", "Inbox", new { lead })
            : RedirectToAction("Index", "Inbox");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await _notifications.MarkAllReadAsync(User.UserId() ?? 0, ct);
        return Ok();
    }
}
