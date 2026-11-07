using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services.Inbox;

/// <summary>Mesajlar ekraninda soldaki tek konusma: mail attigimiz bir lead.</summary>
public record Conversation(
    int LeadId, string CompanyName, string? ContactName, string? ContactTitle,
    DateTime LastAt, bool LastFromThem, string? LastSnippet, int Unread, bool HasReply);

/// <summary>Sohbetteki tek mesaj: bizim gonderdigimiz (Ours) ya da onlarin cevabi.</summary>
public record ChatMessage(bool Ours, DateTime At, string? Subject, string Body, string? Meta, bool AutoReply = false, bool Unread = false);

public record ConversationThread(
    int LeadId, int CompanyId, string CompanyName, string? ContactName, string? ContactTitle, string? ContactEmail,
    IReadOnlyList<ChatMessage> Messages);

public static class InboxFilters
{
    public const string All = "all";
    public const string Replied = "replied";
    public const string Waiting = "waiting";
}

/// <summary>
/// Mesajlar: mail gonderilen tum lead'ler (konusma listesi) ve secilen lead'in sohbeti (bizim maillerimiz + gelen cevaplar).
/// </summary>
public class InboxService
{
    private readonly ApplicationDbContext _db;
    private readonly NotificationService _notifications;

    public InboxService(ApplicationDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<List<Conversation>> ConversationsAsync(string? filter, string? search, CancellationToken ct = default)
    {
        var rows = await _db.Leads.AsNoTracking()
            .Where(l => l.SentEmails.Any())
            .Select(l => new
            {
                l.Id,
                Company = l.Company!.Name,
                Contact = l.Contact != null ? l.Contact.Name : null,
                Title = l.Contact != null ? l.Contact.Title : null,
                LastSent = l.SentEmails.Max(e => e.SentAt),
                LastSentSubject = l.SentEmails.OrderByDescending(e => e.SentAt).Select(e => e.Subject).FirstOrDefault(),
                LastReplyAt = _db.InboundEmails.Where(i => i.LeadId == l.Id).Max(i => (DateTime?)i.ReceivedAt),
                LastReplyBody = _db.InboundEmails.Where(i => i.LeadId == l.Id).OrderByDescending(i => i.ReceivedAt).Select(i => i.Body).FirstOrDefault(),
                HasReply = l.RepliedAt != null || _db.InboundEmails.Any(i => i.LeadId == l.Id && i.Kind == InboundEmailKind.Reply),
                Unread = _db.InboundEmails.Count(i => i.LeadId == l.Id && i.Kind == InboundEmailKind.Reply && i.ReadAt == null)
            })
            .ToListAsync(ct);

        var list = rows.Select(r =>
        {
            var fromThem = r.LastReplyAt is { } at && at >= r.LastSent;
            return new Conversation(r.Id, r.Company, r.Contact, r.Title,
                fromThem ? r.LastReplyAt!.Value : r.LastSent, fromThem,
                fromThem ? ReplyText.Preview(r.LastReplyBody) : r.LastSentSubject, r.Unread, r.HasReply);
        });

        list = filter switch
        {
            InboxFilters.Replied => list.Where(c => c.HasReply),
            InboxFilters.Waiting => list.Where(c => !c.HasReply),
            _ => list
        };

        // Kisi adi sifreli saklandigi icin arama bellekte yapilir (liste kucuk).
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = TurkishText.Normalize(search);
            list = list.Where(c => TurkishText.Normalize(c.CompanyName).Contains(q) || TurkishText.Normalize(c.ContactName).Contains(q));
        }

        return list.OrderByDescending(c => c.Unread > 0).ThenByDescending(c => c.LastAt).ToList();
    }

    /// <summary>Sohbeti doner; okuyan kullanici icin cevaplari ve o lead'in bildirimlerini okundu yapar.</summary>
    public async Task<ConversationThread?> ThreadAsync(int leadId, int userId, CancellationToken ct = default)
    {
        var lead = await _db.Leads.AsNoTracking()
            .Include(l => l.Company)
            .Include(l => l.Contact)
            .FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return null;

        var sent = await _db.SentEmails.AsNoTracking()
            .Where(e => e.LeadId == leadId)
            .Select(e => new { e.SentAt, e.Subject, e.Body, e.Method, e.SequenceStepId, e.ToAddress, Sender = e.SentBy != null ? e.SentBy.FullName : null })
            .ToListAsync(ct);
        var inbound = await _db.InboundEmails.Where(i => i.LeadId == leadId).ToListAsync(ct);

        var messages = sent.Select(e => new ChatMessage(true, e.SentAt, e.Subject, e.Body,
                Meta: (e.SequenceStepId is not null ? "Mail dizisi" : e.Sender ?? "Sistem") +
                      (e.Method == SentEmailMethod.Manual ? " · dışarıdan gönderildi" : string.Empty)))
            .Concat(inbound.Select(i => new ChatMessage(false, i.ReceivedAt, i.Subject, i.Body,
                Meta: string.IsNullOrWhiteSpace(i.FromName) ? i.FromAddress : $"{i.FromName} · {i.FromAddress}",
                AutoReply: i.Kind == InboundEmailKind.AutoReply, Unread: i.ReadAt is null)))
            .OrderBy(m => m.At)
            .ToList();

        var now = DateTime.UtcNow;
        var unread = inbound.Where(i => i.ReadAt is null).ToList();
        if (unread.Count > 0)
        {
            foreach (var i in unread) i.ReadAt = now;
            await _db.SaveChangesAsync(ct);
        }
        await _notifications.MarkLeadReadAsync(userId, leadId, ct);

        return new ConversationThread(lead.Id, lead.CompanyId, lead.Company?.Name ?? "—", lead.Contact?.Name, lead.Contact?.Title,
            lead.Contact?.Email, messages);
    }

    /// <summary>Ust menudeki Mesajlar rozeti: okunmamis cevap sayisi.</summary>
    public Task<int> UnreadRepliesAsync(CancellationToken ct = default) =>
        _db.InboundEmails.CountAsync(i => i.Kind == InboundEmailKind.Reply && i.ReadAt == null, ct);
}
