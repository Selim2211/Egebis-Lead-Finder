using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services;

public record ProvisionResult(int Created, int Deactivated, string? Error)
{
    public bool Success => Error is null;
    public static ProvisionResult Skipped => new(0, 0, null);
}

/// <summary>
/// "Biz ne arıyoruz?" profilinden e-posta taslaklarini otomatik hazirlar: genel tanitim, her hedef
/// segment ve takip maili. Profil kaydedilince yalnizca EKSIK olanlar uretilir (yeni segment eklenince
/// onun taslagi gelir); kullanicinin duzenledigi taslaklara dokunulmaz. Uretilen taslaklarin kimlikleri
/// ayarlarda tutulur, boylece "hangisi otomatik uretildi" bilinir.
/// </summary>
public class ProfileTemplateService
{
    private readonly ApplicationDbContext _db;
    private readonly ITemplateWriterAi _ai;
    private readonly ISettingsService _settings;

    public ProfileTemplateService(ApplicationDbContext db, ITemplateWriterAi ai, ISettingsService settings)
    {
        _db = db;
        _ai = ai;
        _settings = settings;
    }

    /// <summary>
    /// Taslaklari uretir. <paramref name="onlyMissing"/> true ise var olan otomatik taslaklar korunur ve yalnizca
    /// eksik olanlar yazilir; false ise hepsi yeniden yazilir. <paramref name="deactivateAll"/> true ise tum eski
    /// taslaklar, false ise yalnizca hic duzenlenmemis baslangic (Egebis) taslaklari pasife alinir.
    /// </summary>
    public async Task<ProvisionResult> ProvisionAsync(BusinessProfile profile, bool onlyMissing, bool deactivateAll,
        CancellationToken ct = default)
    {
        if (!profile.IsConfigured) return ProvisionResult.Skipped;

        var generatedIds = ParseIds(await _settings.GetAsync(SettingKeys.GeneratedTemplateIds, ct));
        var existing = await _db.EmailTemplates.ToListAsync(ct);
        var generated = existing.Where(t => generatedIds.Contains(t.Id)).ToList();

        var wanted = GeminiAiService.TemplateKeysFor(profile).Select(k => k.Key).ToList();
        var missing = onlyMissing
            ? wanted.Where(k => !generated.Any(t => string.Equals(t.Key, k, StringComparison.OrdinalIgnoreCase))).ToList()
            : wanted;
        if (missing.Count == 0) return ProvisionResult.Skipped;

        var result = await _ai.DraftTemplatesAsync(profile, missing, ct);
        if (result.Error is not null) return new ProvisionResult(0, 0, result.Error);

        // Yeniden uretimde eski otomatik taslaklarin yerine yenileri gelir.
        var replaced = onlyMissing ? new List<EmailTemplate>() : generated.Where(t => missing.Contains(t.Key ?? "")).ToList();
        var deactivated = 0;
        foreach (var old in existing.Where(t => t.Active))
        {
            var seedUntouched = old.UpdatedAt is null && !generatedIds.Contains(old.Id);
            if (deactivateAll || seedUntouched || replaced.Contains(old))
            {
                old.Active = false;
                deactivated++;
            }
        }

        var nextId = (existing.Count == 0 ? 0 : existing.Max(t => t.Id)) + 1;
        var created = new List<EmailTemplate>();
        foreach (var draft in result.Templates)
        {
            var template = new EmailTemplate
            {
                Id = nextId++,
                Name = draft.Name,
                Subject = draft.Subject,
                Body = draft.Body,
                Key = EmailTemplate.IsKnownKey(draft.Key, profile) ? draft.Key : null,
                Active = true,
                UpdatedAt = DateTime.UtcNow
            };
            StringLengthGuard.Apply(template);
            created.Add(template);
        }

        _db.EmailTemplates.AddRange(created);
        await _db.SaveChangesAsync(ct);

        generatedIds.ExceptWith(replaced.Select(t => t.Id));
        foreach (var t in created) generatedIds.Add(t.Id);
        await _settings.SetManyAsync(new Dictionary<string, string?>
        {
            [SettingKeys.GeneratedTemplateIds] = string.Join(",", generatedIds.OrderBy(i => i))
        }, ct);

        return new ProvisionResult(created.Count, deactivated, null);
    }

    private static HashSet<int> ParseIds(string? raw) =>
        (raw ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var i) ? i : 0).Where(i => i > 0).ToHashSet();
}
