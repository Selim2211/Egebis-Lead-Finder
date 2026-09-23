using System.Net;
using System.Text;
using System.Text.Json;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services;

/// <summary>AI #4: kisiye ozel e-posta. GeminiAiService uygular.</summary>
public interface IEmailWriterAi
{
    Task<EmailDraftAiResult> WriteEmailAsync(EmailDraftInput input, CancellationToken ct = default);
}

public class EmailDraftInput
{
    public Company Company { get; init; } = null!;
    public Contact? Contact { get; init; }
    public CompanyAnalysis? Analysis { get; init; }
    public CompanyRating? Rating { get; init; }
    public string SenderName { get; init; } = "Egebis Bilişim";

    /// <summary>Secili sablonun doldurulmus hali; AI ton/yapi ornegi olarak kullanir.</summary>
    public string? TemplateSubject { get; init; }
    public string? TemplateBody { get; init; }

    public bool IsFollowUp { get; init; }

    /// <summary>Daha once gonderilen maillerin konu + tarih ozeti (en yeni once).</summary>
    public List<string> PreviousEmails { get; init; } = new();

    /// <summary>Mailin dili (ISO kodu: tr, en, de...). Firma ulkesinden gelir.</summary>
    public string Language { get; init; } = "tr";
}

public class EmailDraftAiResult
{
    public string? Subject { get; init; }
    public string? Body { get; init; }
    public string? Error { get; init; }
    public bool Success => !string.IsNullOrWhiteSpace(Subject) && !string.IsNullOrWhiteSpace(Body);

    public static EmailDraftAiResult Failed(string error) => new() { Error = error };
}

public record EmailDraft(bool Success, string? Subject, string? BodyHtml, string? Error, bool HasDeepAnalysis);

/// <summary>Lead icin AI e-posta taslagi hazirlar (Compose ekrani ve otomatik diziler).</summary>
public class EmailDraftService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ApplicationDbContext _db;
    private readonly IEmailWriterAi _ai;
    private readonly EmailTemplateService _templates;
    private readonly IEmailSender _sender;

    public EmailDraftService(ApplicationDbContext db, IEmailWriterAi ai, EmailTemplateService templates, IEmailSender sender)
    {
        _db = db;
        _ai = ai;
        _templates = templates;
        _sender = sender;
    }

    public async Task<EmailDraft> DraftAsync(int leadId, int? templateId, bool? followUp = null, CancellationToken ct = default)
    {
        var lead = await _db.Leads.AsNoTracking()
            .Include(l => l.Company)
            .Include(l => l.Contact)
            .Include(l => l.SentEmails)
            .FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead?.Company is null) return new EmailDraft(false, null, null, "Lead bulunamadı.", false);

        var template = templateId is null ? null : await _db.EmailTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);

        (string? Subject, string? Body) rendered = template is null ? (null, null) : _templates.Render(template, lead.Company, lead.Contact);
        var rating = Parse<CompanyRating>(lead.Company.RatingJson);
        var sender = await _sender.GetSettingsAsync(ct);

        var input = new EmailDraftInput
        {
            Company = lead.Company,
            Contact = lead.Contact,
            Analysis = Parse<CompanyAnalysis>(lead.Company.AiAnalysis),
            Rating = rating,
            SenderName = string.IsNullOrWhiteSpace(sender.FromName) ? "Egebis Bilişim" : sender.FromName,
            TemplateSubject = rendered.Subject,
            TemplateBody = rendered.Body,
            IsFollowUp = followUp ?? lead.SentEmails.Count > 0,
            PreviousEmails = lead.SentEmails.OrderByDescending(e => e.SentAt).Take(5)
                .Select(e => $"{e.SentAt.ToLocalTime():dd.MM.yyyy} — {e.Subject}").ToList(),
            Language = SearchRegions.ByCountry(lead.Company.Country)?.Hl ?? "tr"
        };

        var result = await _ai.WriteEmailAsync(input, ct);
        return result.Success
            ? new EmailDraft(true, result.Subject!.Trim(), ToHtml(result.Body!), null, rating?.Summary is not null)
            : new EmailDraft(false, null, null, result.Error ?? "Taslak üretilemedi.", rating?.Summary is not null);
    }

    /// <summary>AI duz metin dondurur; paragraflar &lt;p&gt;, satir sonlari &lt;br&gt; olur (HTML kodlanarak).</summary>
    public static string ToHtml(string text)
    {
        var sb = new StringBuilder();
        foreach (var para in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = para.Split('\n').Select(l => WebUtility.HtmlEncode(l.TrimEnd()));
            sb.Append("<p>").Append(string.Join("<br>", lines)).Append("</p>");
        }
        return sb.ToString();
    }

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
