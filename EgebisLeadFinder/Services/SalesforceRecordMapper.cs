using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Egebis kayitlarini Salesforce alanlarina cevirir. Null deger Salesforce'taki
/// alani temizler: Egebis tarafinda silinen bilgi CRM'de de kalmaz.
/// </summary>
public static class SalesforceRecordMapper
{
    private const int LongText = 32000;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static Dictionary<string, object?> Account(Company company)
    {
        var analysis = ParseAnalysis(company.AiAnalysis);
        var rating = ParseRating(company.RatingJson);

        return new Dictionary<string, object?>
        {
            ["Name"] = Truncate(company.Name, 255),
            ["Website"] = Truncate(company.Website, 255),
            ["BillingCity"] = Truncate(company.City, 40),
            // BillingCountry bilincli olarak gonderilmez: yeni org'larda ulke secim listesi
            // acik gelir ve "Almanya" gibi Turkce adlar tum Account kaydini dusurur.
            ["Egebis_Country__c"] = Truncate(company.Country, 80),
            ["Egebis_Industry__c"] = Truncate(analysis?.Industry ?? company.Industry, 255),
            ["Egebis_NACE__c"] = company.NaceCode,
            ["Description"] = Truncate(company.Description, LongText),
            ["Egebis_Score__c"] = company.Score,
            ["Egebis_Signal__c"] = MapSignal(company.RatingSignal),
            ["Egebis_Stage__c"] = StageLabel(company),
            ["Egebis_Confidence__c"] = rating?.Confidence,
            ["Egebis_Analyzed_At__c"] = FormatDate(company.RatedAt),
            ["Egebis_AI_Summary__c"] = Truncate(BuildSummary(company, analysis, rating), LongText),
            ["Egebis_Sales_Approach__c"] = Truncate(rating?.SalesApproach, LongText),
            ["Egebis_Opportunities__c"] = Truncate(Opportunities(rating), LongText),
            ["Egebis_Financials__c"] = Truncate(Financials(rating), LongText),
            ["Egebis_Management__c"] = Truncate(Management(rating), LongText),
            ["Egebis_Technology__c"] = Truncate(Technology(rating, analysis), LongText),
            ["Egebis_Risks__c"] = Truncate(Risks(rating), LongText),
            ["Egebis_News__c"] = Truncate(News(rating), LongText)
        };
    }

    public static Dictionary<string, object?> Contact(Contact contact, string accountId)
    {
        var (first, last) = SplitName(contact.Name!);
        return new Dictionary<string, object?>
        {
            ["AccountId"] = accountId,
            ["FirstName"] = Truncate(first, 40),
            ["LastName"] = Truncate(string.IsNullOrWhiteSpace(last) ? "(Bilinmiyor)" : last, 80),
            ["Title"] = Truncate(contact.Title, 128),
            ["Email"] = ValidEmail(contact.Email),
            ["Egebis_Email_Status__c"] = EmailStatusDisplay.Label(contact.EmailStatus, contact.Email),
            ["Phone"] = Truncate(contact.Phone, 40),
            ["Description"] = Truncate(ContactNotes(contact), LongText)
        };
    }

    public static Dictionary<string, object?> Lead(Lead lead)
    {
        var contactName = lead.Contact?.Name;
        var (first, last) = string.IsNullOrWhiteSpace(contactName) ? (null, "(Bilinmiyor)") : SplitName(contactName);
        var company = lead.Company;
        var analysis = ParseAnalysis(company?.AiAnalysis);
        var rating = ParseRating(company?.RatingJson);
        var lastEmail = lead.SentEmails.Count > 0 ? lead.SentEmails.Max(e => e.SentAt) : lead.SentAt;

        return new Dictionary<string, object?>
        {
            ["FirstName"] = Truncate(first, 40),
            ["LastName"] = Truncate(string.IsNullOrWhiteSpace(last) ? "(Bilinmiyor)" : last, 80),
            ["Company"] = Truncate(company?.Name ?? "(Bilinmiyor)", 255),
            ["Title"] = Truncate(lead.Contact?.Title, 128),
            ["Email"] = ValidEmail(lead.Contact?.Email),
            ["Egebis_Email_Status__c"] = lead.Contact is null ? null : EmailStatusDisplay.Label(lead.Contact.EmailStatus, lead.Contact.Email),
            ["Egebis_NACE__c"] = company?.NaceCode,
            ["Phone"] = Truncate(lead.Contact?.Phone, 40),
            ["Website"] = Truncate(company?.Website, 255),
            ["City"] = Truncate(company?.City, 40),
            ["Description"] = Truncate(lead.Notes, LongText),
            // Salesforce'un standart Status alani sinirli bir secim listesidir (Open - Not
            // Contacted vb.); bizim durumlarimiz ayri alanda tutulur, Status org varsayilaninda kalir.
            ["Egebis_Status__c"] = LeadStatusDisplay.Label(lead.Status),
            ["Egebis_Score__c"] = lead.Score,
            ["Egebis_Signal__c"] = company is null ? null : MapSignal(company.RatingSignal),
            ["Egebis_Last_Email_At__c"] = FormatDate(lastEmail),
            ["Egebis_AI_Summary__c"] = company is null ? null : Truncate(BuildSummary(company, analysis, rating), LongText),
            ["Egebis_Sales_Approach__c"] = Truncate(rating?.SalesApproach, LongText),
            ["Egebis_Opportunities__c"] = Truncate(Opportunities(rating), LongText)
        };
    }

    // --- Metin derleyiciler ---

    private static string? BuildSummary(Company company, CompanyAnalysis? analysis, CompanyRating? rating)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(rating?.Summary)) parts.Add(rating.Summary.Trim());

        if (analysis is not null)
        {
            var line = new List<string>();
            if (!string.IsNullOrWhiteSpace(analysis.Industry)) line.Add($"Sektör: {analysis.Industry}");
            line.Add(analysis.Manufacturer ? "Üretici firma" : "Üretici değil");
            line.Add(analysis.Sap switch
            {
                "yes" => "SAP kullanıyor",
                "likely" => "SAP kullanma ihtimali yüksek",
                "no" => "SAP kullanmıyor",
                _ => "SAP kullanımı bilinmiyor"
            });
            if (analysis.Products.Count > 0) line.Add("Ürünler: " + string.Join(", ", analysis.Products.Take(5)));
            parts.Add(string.Join(" · ", line));
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : company.Description;
    }

    private static string? Opportunities(CompanyRating? r) =>
        r is null || r.Opportunities.Count == 0
            ? null
            : Lines(r.Opportunities.Select(o =>
                Bullet(o.Text + (string.IsNullOrWhiteSpace(o.Reason) ? "" : $" — {o.Reason}"), o.SourceUrl)));

    private static string? Financials(CompanyRating? r)
    {
        if (r is null) return null;
        var sb = new StringBuilder();
        if (Has(r.FinancialInfo)) sb.AppendLine($"Finansal durum: {r.FinancialInfo} (kaynak: {r.FinancialSource})");
        foreach (var p in r.FinancialPeriods)
            sb.AppendLine(Bullet($"{p.Period}: ciro {Dash(p.Revenue)}, net kâr {Dash(p.NetProfit)}", p.SourceUrl));
        if (Has(r.ScaleInfo)) sb.AppendLine($"Ölçek: {r.ScaleInfo}");
        if (Has(r.SizeInfo?.Employees)) sb.AppendLine($"Çalışan: {r.SizeInfo!.Employees}");
        if (Has(r.SizeInfo?.ExportInfo)) sb.AppendLine($"İhracat: {r.SizeInfo!.ExportInfo}");
        if (Has(r.SizeInfo?.Capacity)) sb.AppendLine($"Kapasite: {r.SizeInfo!.Capacity}");
        if (r.SizeInfo is { Locations.Count: > 0 }) sb.AppendLine($"Lokasyonlar: {string.Join(", ", r.SizeInfo.Locations)}");
        return Empty(sb);
    }

    private static string? Management(CompanyRating? r)
    {
        if (r is null) return null;
        var sb = new StringBuilder();
        foreach (var m in r.Management)
            sb.AppendLine(Bullet(m.Name + (string.IsNullOrWhiteSpace(m.Role) ? "" : $" — {m.Role}"), m.SourceUrl));
        if (Has(r.Owners)) sb.AppendLine($"Ortaklık yapısı: {r.Owners}");
        if (r.GroupCompanies.Count > 0) sb.AppendLine($"Grup / iştirakler: {string.Join(", ", r.GroupCompanies)}");
        return Empty(sb);
    }

    private static string? Technology(CompanyRating? r, CompanyAnalysis? analysis)
    {
        var sb = new StringBuilder();
        var t = r?.Technology;
        if (t is not null && !t.IsEmpty)
        {
            sb.AppendLine($"ERP: {Dash(t.Erp)}" + (Has(t.ErpEvidence) ? $" ({t.ErpEvidence})" : ""));
            if (t.Software.Count > 0) sb.AppendLine($"Yazılımlar: {string.Join(", ", t.Software)}");
            if (t.DigitalProjects.Count > 0) sb.AppendLine($"Dijital projeler: {string.Join("; ", t.DigitalProjects)}");
            if (t.ItJobSignals.Count > 0) sb.AppendLine($"IT ilanları: {string.Join("; ", t.ItJobSignals)}");
        }
        if (Has(analysis?.SapEvidence)) sb.AppendLine($"SAP kanıtı (site): {analysis!.SapEvidence}");
        return Empty(sb);
    }

    private static string? Risks(CompanyRating? r) =>
        r is null ? null
        : r.RiskSignals.Count == 0 ? (r.Summary is null ? null : "Risk işareti bulunamadı.")
        : Lines(r.RiskSignals.Select(x => Bullet($"[{x.Severity}] {x.Text}", x.SourceUrl)));

    private static string? News(CompanyRating? r) =>
        r is null || r.NewsTimeline.Count == 0
            ? null
            : Lines(r.NewsTimeline.Select(n => Bullet((Has(n.Date) ? $"{n.Date} · " : "") + n.Title, n.Url)));

    private static string? ContactNotes(Contact c)
    {
        var parts = new List<string>();
        if (Has(c.Headline)) parts.Add(c.Headline!);
        if (Has(c.Location)) parts.Add($"Konum: {c.Location}");
        if (Has(c.SourceUrl)) parts.Add($"Kaynak: {c.SourceUrl}");
        parts.Add($"Egebis kaynağı: {c.Source}");
        return string.Join("\n", parts);
    }

    public static string StageLabel(Company c) =>
        c.ProjectStartedAt is not null ? "Proje başladı"
        : c.EmailSentAt is not null ? "Mail atıldı"
        : c.ContactedAt is not null ? "İletişim kuruldu"
        : "Temas yok";

    // --- Yardimcilar ---

    private static string Bullet(string text, string? url) =>
        "• " + text + (string.IsNullOrWhiteSpace(url) ? "" : $" ({url})");

    private static string? Lines(IEnumerable<string> lines)
    {
        var joined = string.Join("\n", lines);
        return joined.Length == 0 ? null : joined;
    }

    private static string? Empty(StringBuilder sb)
    {
        var s = sb.ToString().TrimEnd();
        return s.Length == 0 ? null : s;
    }

    private static bool Has(string? v) =>
        !string.IsNullOrWhiteSpace(v) && !v.Equals("bilinmiyor", StringComparison.OrdinalIgnoreCase);

    private static string Dash(string? v) => Has(v) ? v! : "—";

    /// <summary>Salesforce Email alani gecersiz adresi reddeder ve tum kaydi dusurur; gecersizse gonderilmez.</summary>
    public static string? ValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Contains('*')) return null;
        var trimmed = email.Trim();
        return MailAddress.TryCreate(trimmed, out var addr) && addr.Address == trimmed && trimmed.Length <= 80
            ? trimmed
            : null;
    }

    private static string? FormatDate(DateTime? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string? MapSignal(string? ratingSignal) => RatingSignalDisplay.Parse(ratingSignal) switch
    {
        RatingSignal.Guclu => "Güçlü",
        RatingSignal.Incelenmeli => "İncelenmeli",
        RatingSignal.Riskli => "Riskli",
        _ => "Bilinmiyor"
    };

    public static (string? First, string Last) SplitName(string fullName)
    {
        var trimmed = fullName.Trim();
        var idx = trimmed.LastIndexOf(' ');
        return idx <= 0 ? (null, trimmed) : (trimmed[..idx], trimmed[(idx + 1)..]);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    private static CompanyAnalysis? ParseAnalysis(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CompanyAnalysis>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static CompanyRating? ParseRating(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CompanyRating>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
