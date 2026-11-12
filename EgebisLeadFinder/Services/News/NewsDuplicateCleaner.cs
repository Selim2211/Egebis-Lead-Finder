using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services.News;

/// <summary>
/// Veritabanındaki aynı olayı anlatan haber kopyalarını temizler: her gruptan en iyi tek haber kalır, diğerleri silinir.
/// Kalan haber grubun en yüksek önemini ve (varsa) bağlı firmasını alır.
/// </summary>
public static class NewsDuplicateCleaner
{
    public sealed record Candidate(long Id, int? CompanyId, int Importance, bool HasSummary, DateTime PublishedAt);

    /// <summary>Gruptan kalacak haber: firmaya bağlı olan, sonra önemi yüksek, sonra özeti olan, sonra ilk yayımlanan.</summary>
    public static Candidate PickKeeper(IEnumerable<Candidate> group) =>
        group.OrderByDescending(c => c.CompanyId is not null)
            .ThenByDescending(c => c.Importance)
            .ThenByDescending(c => c.HasSummary)
            .ThenBy(c => c.PublishedAt)
            .ThenBy(c => c.Id)
            .First();

    /// <summary>Son <paramref name="days"/> günün haberlerini tarar; silinen kopya sayısını döner.</summary>
    public static async Task<int> RunAsync(ApplicationDbContext db, DateTime nowUtc, CancellationToken ct, int days = 7)
    {
        var since = nowUtc.AddDays(-days);
        var rows = await db.NewsItems.AsNoTracking().Where(n => n.PublishedAt >= since)
            .OrderBy(n => n.PublishedAt).ThenBy(n => n.Id)
            .Select(n => new { n.Id, n.Title, n.Summary, n.PublishedAt, n.CompanyId, n.Importance, n.Category })
            .ToListAsync(ct);
        if (rows.Count < 2) return 0;

        var items = rows.Select(r => new StoryItem(r.Id, r.PublishedAt, NewsSimilarity.Print(r.Title, r.Summary))).ToList();
        var clusters = NewsSimilarity.Clusters(items);
        if (clusters.Count == 0) return 0;

        var ids = clusters.SelectMany(c => c.Select(s => s.Id)).ToList();
        var tracked = await db.NewsItems.Where(n => ids.Contains(n.Id)).ToDictionaryAsync(n => n.Id, ct);
        var removed = 0;
        foreach (var cluster in clusters)
        {
            var group = cluster.Where(s => tracked.ContainsKey(s.Id)).Select(s => tracked[s.Id]).ToList();
            if (group.Count < 2) continue;
            var keeperId = PickKeeper(group.Select(n => new Candidate(n.Id, n.CompanyId, n.Importance,
                !string.IsNullOrWhiteSpace(n.Summary) && n.Summary != n.Title, n.PublishedAt))).Id;
            var keeper = group.First(n => n.Id == keeperId);

            var top = group.OrderByDescending(n => n.Importance).First();
            if (top.Importance > keeper.Importance) { keeper.Importance = top.Importance; keeper.Category = top.Category; }
            if (keeper.CompanyId is null && group.FirstOrDefault(n => n.CompanyId is not null) is { } linked)
            {
                keeper.CompanyId = linked.CompanyId;
                keeper.Origin = NewsOrigins.Watch;
            }
            var drop = group.Where(n => n.Id != keeperId).ToList();
            db.NewsItems.RemoveRange(drop);
            removed += drop.Count;
        }
        await db.SaveChangesAsync(ct);
        return removed;
    }
}
