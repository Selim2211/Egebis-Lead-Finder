using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EgebisLeadFinder.Data;

/// <summary>
/// Kaydedilen degisikliklerden Salesforce'ta eskiyen kayitlari bulur ve
/// SalesforceDirty ile isaretler. Yalnizca Salesforce* alanlari degistiyse
/// (senkronun kendi yazdigi durum) isaretlenmez; aksi halde sonsuz dongu olur.
/// </summary>
public static class SalesforceChangeTracker
{
    public sealed class Pending
    {
        public List<Company> Companies { get; } = new();
        public List<Contact> Contacts { get; } = new();
        public List<Lead> Leads { get; } = new();
        public List<Lead> NewLeads { get; } = new();
        public List<SentEmail> SentEmails { get; } = new();

        public bool IsEmpty =>
            Companies.Count == 0 && Contacts.Count == 0 && Leads.Count == 0 && SentEmails.Count == 0;
    }

    public static Pending Collect(ChangeTracker tracker)
    {
        var pending = new Pending();

        foreach (var entry in tracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (entry.State == EntityState.Modified && !HasBusinessChange(entry)) continue;

            switch (entry.Entity)
            {
                case Company c when entry.State != EntityState.Deleted:
                    pending.Companies.Add(c);
                    break;
                case Contact c:
                    pending.Contacts.Add(c);
                    break;
                case Lead l when entry.State != EntityState.Deleted:
                    pending.Leads.Add(l);
                    if (entry.State == EntityState.Added) pending.NewLeads.Add(l);
                    break;
                case SentEmail e when entry.State == EntityState.Added:
                    pending.SentEmails.Add(e);
                    break;
            }
        }

        return pending;
    }

    /// <summary>Kayit sonrasi calisir: eklenen kayitlarin Id'leri artik bellidir.</summary>
    public static async Task MarkDirtyAsync(ApplicationDbContext db, Pending pending, CancellationToken ct)
    {
        // Lead eklenmesi firmayi otomatik senkron kapsamina alir.
        var companyIds = pending.Companies.Select(c => c.Id)
            .Concat(pending.Contacts.Select(c => c.CompanyId))
            .Concat(pending.NewLeads.Select(l => l.CompanyId))
            .Where(id => id > 0).Distinct().ToList();

        var contactIds = pending.Contacts.Select(c => c.Id).Where(id => id > 0).Distinct().ToList();

        var leadIds = pending.Leads.Select(l => l.Id)
            .Concat(pending.SentEmails.Select(e => e.LeadId))
            .Where(id => id > 0).Distinct().ToList();

        if (companyIds.Count > 0)
            await db.Companies.Where(c => companyIds.Contains(c.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.SalesforceDirty, true), ct);

        // Lead kaydi firma ozetini/firsatlarini ve kisinin e-postasini da tasir.
        if (companyIds.Count > 0 || contactIds.Count > 0 || leadIds.Count > 0)
            await db.Leads.Where(l => leadIds.Contains(l.Id)
                                      || companyIds.Contains(l.CompanyId)
                                      || (l.ContactId != null && contactIds.Contains(l.ContactId.Value)))
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.SalesforceDirty, true), ct);
    }

    private static bool HasBusinessChange(EntityEntry entry) =>
        entry.Properties.Any(p => p.IsModified && !p.Metadata.Name.StartsWith("Salesforce", StringComparison.Ordinal));
}
