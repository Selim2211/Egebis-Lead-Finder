using System.Text.Json;

namespace EgebisLeadFinder.Services;

/// <summary>Yazilan e-postanin konusunu ve govdesini baska bir dile cevirir.</summary>
public interface IMailTranslatorAi
{
    Task<MailTranslation> TranslateMailAsync(string subject, string bodyHtml, string targetLanguage, CancellationToken ct = default);
}

public class MailTranslation
{
    public string? Subject { get; init; }
    public string? BodyHtml { get; init; }
    public string? Error { get; init; }
    public bool Success => Error is null && BodyHtml is not null;
}

public static class MailLanguages
{
    /// <summary>Cevirinin hedef dilleri (kod -> yapay zekaya verilen ad).</summary>
    public static readonly IReadOnlyDictionary<string, string> Targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English",
        ["de"] = "German (Deutsch)",
        ["tr"] = "Turkish (Türkçe)",
        ["fr"] = "French (Français)",
        ["es"] = "Spanish (Español)"
    };

    public const int MaxLength = 20000;
}

public partial class GeminiAiService : IMailTranslatorAi
{
    public async Task<MailTranslation> TranslateMailAsync(string subject, string bodyHtml, string targetLanguage, CancellationToken ct = default)
    {
        if (!MailLanguages.Targets.TryGetValue(targetLanguage ?? string.Empty, out var language))
            return new MailTranslation { Error = "Desteklenmeyen dil." };

        var schema = new
        {
            type = "object",
            properties = new
            {
                subject = new { type = "string", description = "Translated subject line" },
                bodyHtml = new { type = "string", description = "Translated HTML body, same tags and attributes as the input" }
            },
            required = new[] { "subject", "bodyHtml" }
        };

        var prompt = $$"""
            You are a professional business translator. Translate the email subject and the HTML email body into {{language}}.
            Rules:
            - Keep the HTML structure exactly: same tags, attributes, styles, links and <img> elements. Translate only the visible text.
            - Never change, translate or remove placeholders such as {CONTACT_NAME}, {COMPANY_NAME}, {INDUSTRY}, {CITY}, {CONTACT_TITLE}.
            - Keep person names, company names, product names, URLs and email addresses unchanged.
            - Keep the meaning, tone and level of formality; use the natural business greeting and closing of the target language.
            - Do not add, summarise or explain anything. If the text is already in {{language}}, return it unchanged.
            - The subject must stay a single line.
            """;

        var user = "SUBJECT:\n" + subject + "\n\nBODY (HTML):\n" + bodyHtml;
        // localize:false -> arayuz dili istemi etkilemez; hedef dil yalnizca kullanicinin sectigi dildir.
        var (json, error) = await AskJsonAsync(prompt, user, schema, 0.2, ct, localize: false);
        if (error is not null) return new MailTranslation { Error = error };

        try
        {
            using var doc = JsonDocument.Parse(json!);
            var root = doc.RootElement;
            var translatedSubject = root.TryGetProperty("subject", out var s) ? s.GetString() : null;
            var translatedBody = root.TryGetProperty("bodyHtml", out var b) ? b.GetString() : null;
            if (string.IsNullOrWhiteSpace(translatedBody)) return new MailTranslation { Error = "Çeviri boş döndü." };
            return new MailTranslation { Subject = (translatedSubject ?? subject).ReplaceLineEndings(" ").Trim(), BodyHtml = translatedBody };
        }
        catch (JsonException)
        {
            return new MailTranslation { Error = "Çeviri yanıtı okunamadı." };
        }
    }
}
