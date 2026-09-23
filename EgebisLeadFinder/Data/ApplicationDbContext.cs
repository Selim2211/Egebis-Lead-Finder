using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<ApiUsage> ApiUsages => Set<ApiUsage>();
    public DbSet<ApiUsageDaily> ApiUsageDailies => Set<ApiUsageDaily>();
    public DbSet<SearchProfile> SearchProfiles => Set<SearchProfile>();
    public DbSet<CompanySearchProfile> CompanySearchProfiles => Set<CompanySearchProfile>();
    public DbSet<SentEmail> SentEmails => Set<SentEmail>();
    public DbSet<EmailImage> EmailImages => Set<EmailImage>();
    public DbSet<EmailSequence> EmailSequences => Set<EmailSequence>();
    public DbSet<SequenceStep> SequenceSteps => Set<SequenceStep>();
    public DbSet<LeadSequence> LeadSequences => Set<LeadSequence>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ResetChangedEmailVerification();
        var pending = SalesforceChangeTracker.Collect(ChangeTracker);
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        if (!pending.IsEmpty) SalesforceChangeTracker.MarkDirtyAsync(this, pending, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        // Firma/kisi/lead degisince Salesforce'taki kopyasi eskir; kaydedilen her degisiklik
        // "senkron bekliyor" olarak isaretlenir (bkz. SalesforceAutoSyncService).
        ResetChangedEmailVerification();
        var pending = SalesforceChangeTracker.Collect(ChangeTracker);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (!pending.IsEmpty) await SalesforceChangeTracker.MarkDirtyAsync(this, pending, cancellationToken);
        return result;
    }

    /// <summary>
    /// E-posta eklenen/degisen kisinin dogrulama sonucu eskir; EmailVerificationWorker
    /// "dogrulanmadi" durumundaki adresleri kisa surede yeniden kontrol eder.
    /// </summary>
    private void ResetChangedEmailVerification()
    {
        foreach (var entry in ChangeTracker.Entries<Contact>())
        {
            var changed = entry.State == EntityState.Added
                ? !string.IsNullOrWhiteSpace(entry.Entity.Email) && entry.Entity.EmailCheckedAt is null
                : entry.State == EntityState.Modified && entry.Property(c => c.Email).IsModified
                  && !entry.Property(c => c.EmailCheckedAt).IsModified;

            if (!changed) continue;
            entry.Entity.EmailStatus = EmailStatus.Unchecked;
            entry.Entity.EmailStatusReason = null;
            entry.Entity.EmailCheckedAt = null;
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<LeadSequence>().HasIndex(x => new { x.Status, x.NextSendAt });
        b.Entity<LeadSequence>().HasIndex(x => x.LeadId);
        b.Entity<SentEmail>().HasIndex(x => x.MessageId);
        b.Entity<SequenceStep>().HasOne(s => s.Template).WithMany().OnDelete(DeleteBehavior.SetNull);

        // Ayni ayar iki kez kaydedilemez; okuma anahtar uzerinden yapiliyor.
        b.Entity<AppSetting>().HasIndex(x => x.Key).IsUnique();

        // Saglayici basina tek sayac satiri.
        b.Entity<ApiUsage>().HasIndex(x => x.Provider).IsUnique();

        // Saglayici + gun basina tek satir.
        b.Entity<ApiUsageDaily>().HasIndex(x => new { x.Provider, x.Date }).IsUnique();

        b.Entity<Company>(e =>
        {
            // AI analizi ilk versiyonda ayri tablolara bolunmuyor, jsonb olarak duruyor.
            e.Property(x => x.AiAnalysis).HasColumnType("jsonb");
            // Faz-II on arastirma ham ciktisi da jsonb.
            e.Property(x => x.RatingJson).HasColumnType("jsonb");
            // Ayni firma iki kez kaydedilemez. Kod tarafinda da kontrol var ama
            // o "once oku sonra yaz" seklinde calisiyor: es zamanli iki arama
            // (kullanici arama butonuna iki kez basarsa) ikisi de bos kontrol
            // gorup ayni firmayi ekleyebiliyor. Tekillik garantisi veritabaninda.
            // Postgres birden fazla NULL'a izin verir; domaini olmayan kayitlar
            // bu kisittan etkilenmez.
            e.HasIndex(x => x.Domain).IsUnique();
            e.HasIndex(x => x.Score);
        });

        b.Entity<Contact>(e =>
        {
            e.HasOne(x => x.Company)
             .WithMany(c => c.Contacts)
             .HasForeignKey(x => x.CompanyId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Lead>(e =>
        {
            e.HasOne(x => x.Company)
             .WithMany(c => c.Leads)
             .HasForeignKey(x => x.CompanyId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Contact)
             .WithMany()
             .HasForeignKey(x => x.ContactId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.SelectedTemplate)
             .WithMany()
             .HasForeignKey(x => x.SelectedTemplateId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => x.Status);
        });

        b.Entity<SearchProfile>().HasIndex(x => x.Name).IsUnique();

        // Lead silinirse e-posta gecmisi de gider (kisi/firma kaydi etkilenmez).
        b.Entity<SentEmail>(e =>
        {
            e.HasOne(x => x.Lead)
             .WithMany(l => l.SentEmails)
             .HasForeignKey(x => x.LeadId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.LeadId, x.SentAt });
        });

        b.Entity<CompanySearchProfile>(e =>
        {
            e.HasIndex(x => new { x.CompanyId, x.SearchProfileId }).IsUnique();

            e.HasOne(x => x.Company)
             .WithMany()
             .HasForeignKey(x => x.CompanyId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.SearchProfile)
             .WithMany(p => p.CompanyLinks)
             .HasForeignKey(x => x.SearchProfileId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EmailTemplate>().HasData(EmailTemplateSeed.All);
    }
}
