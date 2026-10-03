using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

/// <summary>Taslak Duzenleyici: e-posta sablonlarini listeler, duzenler, ekler, siler.</summary>
public class TemplateController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly BusinessProfileService _profiles;

    public TemplateController(ApplicationDbContext db, BusinessProfileService profiles)
    {
        _db = db;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? id, bool create, CancellationToken ct)
    {
        EmailTemplate current;
        if (create)
        {
            var profile = await _profiles.GetAsync(ct);
            current = new EmailTemplate
            {
                Name = "Yeni taslak",
                Subject = "",
                Body = $"Sayın {{CONTACT_NAME}},\n\n\n\nSaygılarımızla,\n{(profile.IsConfigured ? profile.DisplayName : "Egebis Bilişim")}"
            };
        }
        else
        {
            var found = id is null ? null : await _db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (id is not null && found is null) return RedirectToAction(nameof(Index));
            current = found ?? await _db.EmailTemplates.AsNoTracking().OrderBy(t => t.Id).FirstOrDefaultAsync(ct)
                      ?? new EmailTemplate { Name = "Yeni taslak" };
        }

        return View(await BuildModelAsync(current, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string? name, string? subject, string? key, bool active,
        string? body, string? bodyHtml, CancellationToken ct)
    {
        var html = EmailHtml.Sanitize(bodyHtml);
        var plain = (body ?? string.Empty).Trim();

        var template = id == 0 ? new EmailTemplate() : await _db.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return NotFound();

        template.Name = (name ?? string.Empty).Trim();
        template.Subject = (subject ?? string.Empty).Trim();
        template.Key = EmailTemplate.IsKnownKey(key, await _profiles.GetAsync(ct)) ? key : null;
        template.Body = plain;
        template.BodyHtml = string.IsNullOrEmpty(html) ? null : html;
        template.Active = active;
        template.UpdatedAt = DateTime.UtcNow;

        string? error = null;
        if (template.Name.Length == 0) error = "Taslağa bir ad verin.";
        else if (template.Subject.Length == 0) error = "Konu boş olamaz.";
        else if (plain.Length == 0 && EmailHtml.ImageIds(html).Count == 0) error = "Taslak metni boş olamaz.";
        else if (!active && !await _db.EmailTemplates.AnyAsync(t => t.Active && t.Id != id, ct))
            error = "En az bir aktif taslak kalmalı; e-posta ekranı onu kullanır.";

        if (error is not null)
        {
            if (id != 0) _db.Entry(template).State = EntityState.Detached;
            var model = await BuildModelAsync(template, ct);
            model.BodyHtml = string.IsNullOrEmpty(html) ? EmailHtml.FromPlainText(plain) : html;
            model.Error = error;
            return View(nameof(Index), model);
        }

        StringLengthGuard.Apply(template);

        if (id == 0)
        {
            // Baslangic taslaklari sabit Id ile eklendigi icin kimlik sirasina guvenmiyoruz.
            template.Id = (await _db.EmailTemplates.MaxAsync(t => (int?)t.Id, ct) ?? 0) + 1;
            _db.EmailTemplates.Add(template);
        }

        await _db.SaveChangesAsync(ct);

        TempData["TemplateSaved"] = id == 0 ? "Yeni taslak oluşturuldu." : "Taslak kaydedildi.";
        return RedirectToAction(nameof(Index), new { id = template.Id });
    }

    /// <summary>
    /// "Biz ne arıyoruz?" profilinden yapay zeka ile taslak yazar: genel tanitim, her hedef segment ve
    /// takip maili; segment taslaklari o segmentteki firmalara otomatik onerilir. Mevcut taslaklar
    /// istenirse pasife alinir (silinmez).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateFromProfile(bool deactivateOthers, [FromServices] ProfileTemplateService provisioning,
        CancellationToken ct)
    {
        var profile = await _profiles.GetAsync(ct);
        var result = await provisioning.ProvisionAsync(profile, onlyMissing: false, deactivateAll: deactivateOthers, ct);
        if (!result.Success)
        {
            EgebisLeadFinder.Services.Auth.AuditActionFilter.MarkFailed(HttpContext);
            TempData["TemplateError"] = result.Error;
            return RedirectToAction(nameof(Index));
        }

        EgebisLeadFinder.Services.Auth.AuditActionFilter.SetAuditSummary(HttpContext,
            $"Profilden {result.Created} e-posta taslağı oluşturuldu" + (deactivateOthers ? ", eski taslaklar pasife alındı" : ""));
        TempData["TemplateSaved"] = $"Profilinize göre {result.Created} taslak oluşturuldu"
            + (result.Deactivated > 0 ? $"; {result.Deactivated} eski taslak pasife alındı (silinmedi, istediğinizi yeniden aktif yapabilirsiniz)." : ".")
            + " Göndermeden önce metinleri kontrol edin.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(int id, CancellationToken ct)
    {
        var source = await _db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (source is null) return NotFound();

        var copy = new EmailTemplate
        {
            Id = (await _db.EmailTemplates.MaxAsync(t => (int?)t.Id, ct) ?? 0) + 1,
            Name = source.Name + " (kopya)",
            Subject = source.Subject,
            Body = source.Body,
            BodyHtml = source.BodyHtml,
            Key = null,
            Active = source.Active,
            UpdatedAt = DateTime.UtcNow
        };
        StringLengthGuard.Apply(copy);
        _db.EmailTemplates.Add(copy);
        await _db.SaveChangesAsync(ct);

        TempData["TemplateSaved"] = "Taslağın kopyası oluşturuldu.";
        return RedirectToAction(nameof(Index), new { id = copy.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var template = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return RedirectToAction(nameof(Index));

        if (template.Active && !await _db.EmailTemplates.AnyAsync(t => t.Active && t.Id != id, ct))
        {
            TempData["TemplateError"] = "Son aktif taslak silinemez; e-posta ekranı onu kullanır.";
            return RedirectToAction(nameof(Index), new { id });
        }

        // Lead'lerdeki secim null'a duser; gonderilmis e-postalarin metni gecmiste kalir.
        _db.EmailTemplates.Remove(template);
        await _db.SaveChangesAsync(ct);

        TempData["TemplateSaved"] = $"\"{template.Name}\" silindi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<TemplateEditorViewModel> BuildModelAsync(EmailTemplate current, CancellationToken ct)
    {
        var templates = await _db.EmailTemplates.AsNoTracking()
            .OrderByDescending(t => t.Active).ThenBy(t => t.Name)
            .ToListAsync(ct);

        var sentCounts = await _db.SentEmails.AsNoTracking()
            .Where(e => e.TemplateId != null)
            .GroupBy(e => e.TemplateId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var images = await _db.EmailImages.AsNoTracking()
            .Where(i => i.InLibrary)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new EmailImage { Id = i.Id, Name = i.Name, ContentType = i.ContentType })
            .ToListAsync(ct);

        return new TemplateEditorViewModel
        {
            Templates = templates,
            Current = current,
            BodyHtml = string.IsNullOrWhiteSpace(current.BodyHtml)
                ? EmailHtml.FromPlainText(current.Body)
                : EmailHtml.Sanitize(current.BodyHtml),
            SentCounts = sentCounts,
            Images = images,
            Profile = await _profiles.GetAsync(ct)
        };
    }
}
