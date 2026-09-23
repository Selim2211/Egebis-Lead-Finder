using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

public class EmailController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly EmailTemplateService _templates;
    private readonly IEmailSender _sender;

    public EmailController(
        ApplicationDbContext db,
        EmailTemplateService templates,
        IEmailSender sender)
    {
        _db = db;
        _templates = templates;
        _sender = sender;
    }

    [HttpGet]
    public async Task<IActionResult> Compose(int leadId, int? templateId, CancellationToken ct)
    {
        var model = await BuildModelAsync(leadId, templateId, ct);
        return model is null ? NotFound() : View(model);
    }

    /// <summary>Sablon degistiginde onizlemeyi yeniden uretir.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Compose(int leadId, int templateId, CancellationToken ct)
    {
        var model = await BuildModelAsync(leadId, templateId, ct);
        if (model is null) return NotFound();

        // Sablon secimi lead uzerinde saklanir, e-mail hazir sayilir.
        var lead = await _db.Leads.FirstAsync(l => l.Id == leadId, ct);
        lead.SelectedTemplateId = templateId;
        if (lead.Status < LeadStatus.EmailHazir)
            lead.Status = LeadStatus.EmailHazir;
        await _db.SaveChangesAsync(ct);

        return View(model);
    }

    /// <summary>"AI ile yaz": kisiye ozel konu + govde uretir; editore JS ile yazilir, gonderilmez.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Draft(int leadId, int? templateId, [FromServices] EmailDraftService drafts, CancellationToken ct)
    {
        var draft = await drafts.DraftAsync(leadId, templateId, ct: ct);
        return Json(new
        {
            success = draft.Success,
            subject = draft.Subject,
            bodyHtml = draft.BodyHtml,
            error = draft.Error,
            hint = draft.HasDeepAnalysis ? null : "Firma analizi yapılırsa mail çok daha kişisel olur (Firma → Firma analizi)."
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(int leadId, int templateId, string subject, string body,
        string? bodyHtml, string? toAddress, CancellationToken ct)
    {
        var model = await BuildModelAsync(leadId, templateId, ct);
        if (model is null) return NotFound();

        // Kullanicinin ekranda duzenledigi metin gonderilir, sablonun ham hali degil.
        var html = EmailHtml.Sanitize(bodyHtml);
        model.Subject = subject;
        model.Body = body;
        model.BodyHtml = string.IsNullOrEmpty(html) ? EmailHtml.FromPlainText(body) : html;
        model.ToAddress = toAddress;

        var images = await LoadImagesAsync(html, ct);
        var result = await _sender.SendAsync(toAddress ?? string.Empty, subject, body,
            string.IsNullOrEmpty(html) ? null : html, images, ct);

        if (result.Sent)
        {
            await RecordAsync(leadId, templateId, result.FromAddress, toAddress!, subject, body, html,
                images, SentEmailMethod.Smtp, ct, result.MessageId);
            TempData["LeadSuccess"] = $"E-posta {toAddress} adresine gönderildi ve lead geçmişine kaydedildi.";
            return RedirectToAction("Details", "Lead", new { id = leadId });
        }

        model.StatusMessage = result.Error;
        model.IsError = true;
        return View(nameof(Compose), model);
    }

    /// <summary>
    /// Kullanici maili disarida (Outlook vb.) gonderdiginde isaretler. Ekrandaki
    /// metin de lead gecmisine yazilir, boylece ne gonderildigi kaybolmaz.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkSent(int leadId, int? templateId, string? subject, string? body,
        string? bodyHtml, string? toAddress, CancellationToken ct)
    {
        var exists = await _db.Leads.AnyAsync(l => l.Id == leadId, ct);
        if (!exists) return NotFound();

        var settings = await _sender.GetSettingsAsync(ct);
        var html = EmailHtml.Sanitize(bodyHtml);
        var images = await LoadImagesAsync(html, ct);
        await RecordAsync(leadId, templateId, settings.FromAddress, toAddress ?? string.Empty,
            subject ?? string.Empty, body ?? string.Empty, html, images, SentEmailMethod.Manual, ct);

        TempData["LeadSuccess"] = "Dışarıdan gönderildi olarak işaretlendi; metin lead geçmişine kaydedildi.";
        return RedirectToAction("Details", "Lead", new { id = leadId });
    }

    // ---- Gorsel kutuphanesi (logo vb.) ----

    [HttpGet]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> Image(int id, CancellationToken ct)
    {
        var image = await _db.EmailImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        return image is null ? NotFound() : File(image.Data, image.ContentType);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(EmailImage.MaxBytes + 64 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile? file, string? name, bool inLibrary,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Dosya seçilmedi." });
        if (file.Length > EmailImage.MaxBytes)
            return BadRequest(new { error = "Görsel en fazla 2 MB olabilir." });

        var contentType = file.ContentType.ToLowerInvariant() == "image/jpg" ? "image/jpeg" : file.ContentType.ToLowerInvariant();
        if (!EmailImage.AllowedContentTypes.Contains(contentType))
            return BadRequest(new { error = "Sadece PNG, JPG veya GIF yüklenebilir." });

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        var data = stream.ToArray();
        if (!LooksLikeImage(data))
            return BadRequest(new { error = "Dosya geçerli bir görsel değil." });

        var image = new EmailImage
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file.FileName) : name.Trim(),
            ContentType = contentType,
            Data = data,
            InLibrary = inLibrary,
            SortOrder = (await _db.EmailImages.MaxAsync(i => (int?)i.SortOrder, ct) ?? 0) + 1
        };
        StringLengthGuard.Apply(image);
        _db.EmailImages.Add(image);
        await _db.SaveChangesAsync(ct);

        return Json(ToDto(image));
    }

    /// <summary>
    /// Kayitli gorseller listesinden kaldirir. Veri silinmez: daha once gonderilen
    /// e-postalar lead gecmisinde gorseliyle birlikte gorunmeye devam eder.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveFromLibrary(int id, CancellationToken ct)
    {
        var image = await _db.EmailImages.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (image is null) return NotFound();

        image.InLibrary = false;
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    private object ToDto(EmailImage i) => new
    {
        id = i.Id,
        name = i.Name,
        url = Url.Action(nameof(Image), new { id = i.Id })
    };

    /// <summary>PNG / JPEG / GIF imzasi; uzantiya veya tarayicinin bildirdigi tipe guvenmeyiz.</summary>
    public static bool LooksLikeImage(byte[] d) =>
        d.Length > 8 && (
            (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) ||
            (d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) ||
            (d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x38));

    /// <summary>Metinde gecen gorseller (HTML'deki sirayla).</summary>
    private async Task<List<EmailImage>> LoadImagesAsync(string html, CancellationToken ct)
    {
        var ids = EmailHtml.ImageIds(html);
        if (ids.Count == 0) return new();

        var images = await _db.EmailImages.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .ToListAsync(ct);
        return images.OrderBy(i => ids.IndexOf(i.Id)).ToList();
    }

    private async Task RecordAsync(int leadId, int? templateId, string? from, string to, string subject,
        string body, string html, IReadOnlyList<EmailImage> images, SentEmailMethod method, CancellationToken ct,
        string? messageId = null)
    {
        var lead = await _db.Leads.FirstAsync(l => l.Id == leadId, ct);
        var template = templateId is not null
            ? await _db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct)
            : null;

        SentEmailRecorder.Add(_db, lead, template, from, to, subject, body, html,
            images.Count == 0 ? null : string.Join(", ", images.Select(i => i.Name)), method, messageId, stepId: null);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<EmailViewModel?> BuildModelAsync(int leadId, int? templateId, CancellationToken ct)
    {
        var lead = await _db.Leads
            .Include(l => l.Company)
            .Include(l => l.Contact)
            .FirstOrDefaultAsync(l => l.Id == leadId, ct);

        if (lead?.Company is null) return null;

        var templates = await _db.EmailTemplates
            .Where(t => t.Active)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);

        var analysis = CompanyController.ParseAnalysis(lead.Company.AiAnalysis);

        var history = await _db.SentEmails.AsNoTracking()
            .Where(e => e.LeadId == leadId)
            .OrderByDescending(e => e.SentAt)
            .ToListAsync(ct);

        // Oncelik: kullanicinin sectigi sablon > daha once mail gittiyse takip sablonu
        // > lead'de kayitli secim > AI onerisi.
        var followUp = history.Count > 0
            ? templates.FirstOrDefault(t => t.Key == EmailTemplate.FollowUpKey)
            : null;

        var selected = templateId is not null
            ? templates.FirstOrDefault(t => t.Id == templateId)
            : followUp
              ?? (lead.SelectedTemplateId is not null
                  ? templates.FirstOrDefault(t => t.Id == lead.SelectedTemplateId)
                  : null);

        selected ??= _templates.PickTemplate(templates, analysis?.RecommendedTemplate);

        var (subject, body) = _templates.Render(selected, lead.Company, lead.Contact);
        var sender = await _sender.GetSettingsAsync(ct);

        // Resim verisi yuklenmez; kutucuklar /Email/Image/{id}'den ceker.
        var images = await _db.EmailImages.AsNoTracking()
            .Where(i => i.InLibrary)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => new EmailImage { Id = i.Id, Name = i.Name, ContentType = i.ContentType, SortOrder = i.SortOrder })
            .ToListAsync(ct);

        return new EmailViewModel
        {
            Lead = lead,
            Company = lead.Company,
            Contact = lead.Contact,
            Templates = templates,
            SelectedTemplateId = selected.Id,
            RecommendedTemplateKey = followUp?.Key ?? analysis?.RecommendedTemplate,
            IsFollowUp = followUp is not null,
            RecommendationReason = analysis?.Reason,
            Subject = subject,
            Body = body,
            BodyHtml = _templates.RenderHtml(selected, lead.Company, lead.Contact),
            ToAddress = lead.Contact?.Email,
            SmtpEnabled = sender.IsConfigured,
            SenderDisplay = sender.Display,
            SalutationName = PersonDisplay.SalutationName(lead.Contact),
            History = history,
            Images = images
        };
    }
}
