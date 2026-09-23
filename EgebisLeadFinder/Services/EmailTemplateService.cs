using System.Net;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Sablonu firma ve kisi bilgileriyle doldurur. AI e-mail yazmiyor:
/// duz string degistirme daha ucuz, deterministik ve yanlis bilgi uretmiyor
/// (dokuman bolum 13).
/// </summary>
public class EmailTemplateService
{
    /// <summary>Sablondaki yer tutuculari doldurur.</summary>
    public (string Subject, string Body) Render(EmailTemplate template, Company company, Contact? contact)
    {
        return (Fill(template.Subject, company, contact, encode: false),
            Fill(template.Body, company, contact, encode: false));
    }

    /// <summary>
    /// Editorde gosterilecek bicimli govde. Taslakta HTML (gorsel/bicim) varsa o
    /// doldurulur; yoksa duz metin govde HTML'e cevrilir.
    /// </summary>
    public string RenderHtml(EmailTemplate template, Company company, Contact? contact)
    {
        if (string.IsNullOrWhiteSpace(template.BodyHtml))
            return EmailHtml.FromPlainText(Fill(template.Body, company, contact, encode: false));

        return Fill(EmailHtml.Sanitize(template.BodyHtml), company, contact, encode: true);
    }

    private static string Fill(string text, Company company, Contact? contact, bool encode)
    {
        string V(string value) => encode ? WebUtility.HtmlEncode(value) : value;

        return text.Replace("{CONTACT_NAME}", V(ContactSalutation(contact)))
            .Replace("{CONTACT_TITLE}", V(contact?.Title ?? string.Empty))
            .Replace("{COMPANY_NAME}", V(company.Name))
            .Replace("{INDUSTRY}", V(company.Industry ?? "faaliyet gösterdiğiniz"))
            .Replace("{CITY}", V(company.City ?? string.Empty));
    }

    /// <summary>
    /// Lead kisisinin ad soyadi. Isim yoksa sablon "Sayın ," gibi bozuk cikmasin
    /// diye kurumsal hitap kullanilir.
    /// </summary>
    public static string ContactSalutation(Contact? contact) =>
        PersonDisplay.SalutationName(contact) ?? "Yetkili";

    /// <summary>
    /// AI'in onerdigi sablonu secer. Oneri yoksa veya eslesen sablon bulunamazsa
    /// genel tanitim sablonuna duser.
    /// </summary>
    public EmailTemplate PickTemplate(IReadOnlyCollection<EmailTemplate> templates, string? recommendedKey)
    {
        if (!string.IsNullOrWhiteSpace(recommendedKey))
        {
            var match = templates.FirstOrDefault(t =>
                string.Equals(t.Key, recommendedKey, StringComparison.OrdinalIgnoreCase));

            if (match is not null) return match;
        }

        return templates.FirstOrDefault(t => t.Key == "GENEL") ?? templates.First();
    }
}
