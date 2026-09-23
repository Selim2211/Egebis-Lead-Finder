using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>Gonderilen maili lead gecmisine yazar ve lead'in durumunu gunceller (kaydetmez).</summary>
public static class SentEmailRecorder
{
    public static SentEmail Add(ApplicationDbContext db, Lead lead, EmailTemplate? template, string? from, string to,
        string subject, string body, string? html, string? imageNames, SentEmailMethod method, string? messageId, int? stepId)
    {
        var sentEmail = new SentEmail
        {
            LeadId = lead.Id,
            FromAddress = from,
            ToAddress = to.Trim(),
            Subject = subject,
            Body = body,
            BodyHtml = string.IsNullOrEmpty(html) ? null : html,
            TemplateId = template?.Id,
            TemplateName = template?.Name,
            ImageNames = imageNames,
            Method = method,
            SentAt = DateTime.UtcNow,
            MessageId = messageId,
            SequenceStepId = stepId
        };
        StringLengthGuard.Apply(sentEmail);
        db.SentEmails.Add(sentEmail);

        lead.Status = LeadStatus.Gonderildi;
        // Takip sayaci son gonderimden isler: her yeni e-posta tarihi gunceller.
        lead.SentAt = sentEmail.SentAt;
        lead.SnoozedUntil = null;
        if (template is not null) lead.SelectedTemplateId = template.Id;

        return sentEmail;
    }
}
