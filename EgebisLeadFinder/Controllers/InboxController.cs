using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Auth;
using EgebisLeadFinder.Services.Inbox;
using EgebisLeadFinder.Services.Sequences;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

public class InboxViewModel
{
    public List<Conversation> Conversations { get; set; } = new();
    public ConversationThread? Thread { get; set; }
    public string Filter { get; set; } = InboxFilters.All;
    public string? Search { get; set; }
    public bool ImapConfigured { get; set; }
    public DateTime? LastCheck { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Mesajlar: mail attigimiz herkes (solda) ve secilen kisiyle sohbet (sagda: bizim maillerimiz + gelen cevaplar).
/// Cevaplar gelen kutusundan (IMAP) 5 dakikada bir okunur; "Şimdi kontrol et" hemen okur.
/// </summary>
public class InboxController : Controller
{
    private readonly InboxService _inbox;
    private readonly ISettingsService _settings;

    public InboxController(InboxService inbox, ISettingsService settings)
    {
        _inbox = inbox;
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? lead, string? filter, string? q, [FromServices] ReplyDetectionService replies, CancellationToken ct)
    {
        var model = new InboxViewModel
        {
            Filter = filter is InboxFilters.Replied or InboxFilters.Waiting ? filter : InboxFilters.All,
            Search = q?.Trim()
        };
        model.Conversations = await _inbox.ConversationsAsync(model.Filter, model.Search, ct);

        var selected = lead ?? model.Conversations.FirstOrDefault()?.LeadId;
        if (selected is int id)
            model.Thread = await _inbox.ThreadAsync(id, User.UserId() ?? 0, ct);
        // Acilan sohbetin okunmamis sayisi listede de sifirlansin.
        if (model.Thread is not null)
            model.Conversations = model.Conversations
                .Select(c => c.LeadId == model.Thread.LeadId ? c with { Unread = 0 } : c).ToList();

        model.ImapConfigured = (await replies.ConfigAsync(ct)).IsComplete;
        model.LastCheck = DateTime.TryParse(await _settings.GetAsync(SettingKeys.ImapLastCheck, ct), null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null;
        model.LastError = await _settings.GetAsync(SettingKeys.ImapLastError, ct);
        return View(model);
    }

    /// <summary>Gelen kutusunu hemen okur (5 dakikalik otomatik kontrolu beklemeden).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Check(int? lead, string? filter, [FromServices] ReplyDetectionService replies, CancellationToken ct)
    {
        var cfg = await replies.ConfigAsync(ct);
        if (!cfg.IsComplete)
        {
            TempData["InboxError"] = "Gelen kutusu ayarları eksik: Ayarlar'da e-posta (SMTP/IMAP) bilgilerini girin.";
        }
        else
        {
            var processed = await replies.CheckAsync(ct);
            var error = await _settings.GetAsync(SettingKeys.ImapLastError, ct);
            if (!string.IsNullOrWhiteSpace(error)) TempData["InboxError"] = "Gelen kutusu okunamadı: " + error;
            else TempData["InboxSuccess"] = processed > 0 ? $"Gelen kutusu okundu: {processed} yeni cevap/geri dönüş işlendi." : "Gelen kutusu okundu: yeni cevap yok.";
        }
        return RedirectToAction(nameof(Index), new { lead, filter });
    }
}
