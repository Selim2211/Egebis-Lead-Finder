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

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

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
