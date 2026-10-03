using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Tests;

/// <summary>Profil kaydedilince e-posta taslaklari otomatik hazirlanir; yalnizca eksikler, duzenlenenlere dokunulmaz.</summary>
public class ProfileTemplateServiceTests
{
    private sealed class FakeWriter : ITemplateWriterAi
    {
        public List<string> Requested { get; } = new();
        public string? Error { get; set; }

        public Task<TemplateDraftResult> DraftTemplatesAsync(BusinessProfile profile, IReadOnlyCollection<string>? onlyKeys = null, CancellationToken ct = default)
        {
            if (Error is not null) return Task.FromResult(new TemplateDraftResult { Error = Error });
            var keys = GeminiAiService.TemplateKeysFor(profile).Select(k => k.Key)
                .Where(k => onlyKeys is null || onlyKeys.Contains(k)).ToList();
            Requested.AddRange(keys);
            return Task.FromResult(new TemplateDraftResult
            {
                Templates = keys.Select(k => new TemplateDraft { Key = k, Name = "Taslak " + k, Subject = "Konu " + k, Body = "Sayın {CONTACT_NAME}, " + k }).ToList()
            });
        }
    }

    private static BusinessProfile Profile(params string[] segmentIds) => new()
    {
        CompanyName = "Sey Boru", Offering = "Boru", IdealCustomer = "Tesisat",
        Segments = segmentIds.Select(id => new TargetSegment { Id = id, Name = "Seg " + id }).ToList()
    };

    private static (ApplicationDbContext Db, ProfileTemplateService Service, FakeWriter Writer) Create()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var writer = new FakeWriter();
        return (db, new ProfileTemplateService(db, writer, new FakeSettingsService()), writer);
    }

    [Fact]
    public async Task Ilk_kayitta_taslaklar_uretilir_ve_dokunulmamis_Egebis_taslaklari_pasife_alinir()
    {
        var (db, service, _) = Create();
        db.EmailTemplates.AddRange(
            new EmailTemplate { Id = 1, Name = "SAP", Subject = "s", Body = "b", Key = "SAP", Active = true },
            new EmailTemplate { Id = 2, Name = "Benim taslağım", Subject = "s", Body = "b", Active = true, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await service.ProvisionAsync(Profile("a1"), onlyMissing: true, deactivateAll: false);

        Assert.True(result.Success);
        Assert.Equal(3, result.Created); // genel + 1 segment + takip
        Assert.False((await db.EmailTemplates.FindAsync(1))!.Active);   // dokunulmamis baslangic taslagi
        Assert.True((await db.EmailTemplates.FindAsync(2))!.Active);    // kullanicinin duzenledigi
        Assert.Contains(db.EmailTemplates, t => t.Key == "SEG:a1" && t.Active);
    }

    [Fact]
    public async Task Ikinci_kayitta_yalnizca_yeni_segmentin_taslagi_yazilir()
    {
        var (db, service, writer) = Create();
        await service.ProvisionAsync(Profile("a1"), onlyMissing: true, deactivateAll: false);
        var before = db.EmailTemplates.Count();
        writer.Requested.Clear();

        var again = await service.ProvisionAsync(Profile("a1"), onlyMissing: true, deactivateAll: false);
        Assert.Equal(0, again.Created);
        Assert.Empty(writer.Requested);

        var added = await service.ProvisionAsync(Profile("a1", "b2"), onlyMissing: true, deactivateAll: false);
        Assert.Equal(1, added.Created);
        Assert.Equal(new[] { "SEG:b2" }, writer.Requested);
        Assert.Equal(before + 1, db.EmailTemplates.Count());
    }

    [Fact]
    public async Task Yapay_zeka_hatasinda_hicbir_sey_degismez_ve_profil_yoksa_atlanir()
    {
        var (db, service, writer) = Create();
        db.EmailTemplates.Add(new EmailTemplate { Id = 1, Name = "SAP", Subject = "s", Body = "b", Active = true });
        await db.SaveChangesAsync();

        writer.Error = "Gemini kotası doldu";
        var failed = await service.ProvisionAsync(Profile(), onlyMissing: true, deactivateAll: false);
        Assert.False(failed.Success);
        Assert.True((await db.EmailTemplates.FindAsync(1))!.Active);
        Assert.Equal(1, db.EmailTemplates.Count());

        var skipped = await service.ProvisionAsync(new BusinessProfile(), onlyMissing: true, deactivateAll: false);
        Assert.True(skipped.Success);
        Assert.Equal(0, skipped.Created);
    }
}
