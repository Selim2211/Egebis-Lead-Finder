using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EgebisLeadFinder.Services.Inbox;

/// <summary>
/// Sag ustteki zil: bildirim olusturma, okunmamis sayisi (kullanici basina kisa onbellek), liste, okundu isareti.
/// </summary>
public class NotificationService
{
    private static readonly TimeSpan CountCache = TimeSpan.FromSeconds(20);
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public NotificationService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public static string CountKey(int userId) => "notif-count:" + userId;

    /// <summary>
    /// "Cevap geldi" bildirimi. Alici: maili gonderen kullanici; otomatik dizi maili (gonderen yok) ya da gonderen
    /// pasifse tum aktif kullanicilar. Kaydetmez (cagiran SaveChanges yapar).
    /// </summary>
    public async Task AddReplyAsync(Lead lead, int? senderUserId, string? personName, string preview, CancellationToken ct = default)
    {
        var recipients = new List<int>();
        if (senderUserId is int sender && await _db.Users.AnyAsync(u => u.Id == sender && u.IsActive, ct))
            recipients.Add(sender);
        else
            recipients = await _db.Users.Where(u => u.IsActive).Select(u => u.Id).ToListAsync(ct);

        var company = lead.Company?.Name ?? await _db.Companies.Where(c => c.Id == lead.CompanyId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        foreach (var userId in recipients.Distinct())
        {
            _db.Notifications.Add(new Notification
            {
                UserId = userId,
                Kind = NotificationKinds.Reply,
                LeadId = lead.Id,
                CompanyName = company is { Length: > 300 } ? company[..300] : company,
                PersonName = personName,
                Preview = preview
            });
            _cache.Remove(CountKey(userId));
        }
    }

    public async Task<int> UnreadCountAsync(int userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CountKey(userId), out int cached)) return cached;
        var count = await _db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
        _cache.Set(CountKey(userId), count, CountCache);
        return count;
    }

    public Task<List<Notification>> LatestAsync(int userId, int take = 12, CancellationToken ct = default) =>
        _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderBy(n => n.ReadAt != null).ThenByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    /// <summary>Bildirimi okundu yapar ve gidilecek lead'i doner (baskasinin bildirimi ise null).</summary>
    public async Task<int?> OpenAsync(int userId, int id, CancellationToken ct = default)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (n is null) return null;
        n.ReadAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _cache.Remove(CountKey(userId));
        return n.LeadId;
    }

    public Task MarkAllReadAsync(int userId, CancellationToken ct = default) =>
        MarkReadAsync(_db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null), userId, ct);

    /// <summary>Sohbet acilinca o lead'e ait bildirimler de okunmus sayilir.</summary>
    public Task MarkLeadReadAsync(int userId, int leadId, CancellationToken ct = default) =>
        MarkReadAsync(_db.Notifications.Where(n => n.UserId == userId && n.LeadId == leadId && n.ReadAt == null), userId, ct);

    private async Task MarkReadAsync(IQueryable<Notification> query, int userId, CancellationToken ct)
    {
        var unread = await query.ToListAsync(ct);
        if (unread.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var n in unread) n.ReadAt = now;
        await _db.SaveChangesAsync(ct);
        _cache.Remove(CountKey(userId));
    }
}
