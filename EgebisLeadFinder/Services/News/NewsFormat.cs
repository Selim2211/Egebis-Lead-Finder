using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services.News;

/// <summary>Haber ekranlarının küçük görüntü yardımcıları (testlenebilsin diye görünümlerden ayrı).</summary>
public static class NewsFormat
{
    public static string Ago(DateTime utc, DateTime? nowUtc = null)
    {
        var d = (nowUtc ?? DateTime.UtcNow) - utc;
        return d.TotalMinutes < 1 ? "az önce"
            : d.TotalMinutes < 60 ? $"{(int)d.TotalMinutes} dk önce"
            : d.TotalHours < 24 ? $"{(int)d.TotalHours} sa önce"
            : utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }

    /// <summary>Firma adından 2 harflik rozet (ör. "Vestel Elektronik" → "VE").</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "•";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}" : parts[0][..Math.Min(2, parts[0].Length)];
        return s.ToUpper(new System.Globalization.CultureInfo("tr-TR"));
    }

    /// <summary>Haberin konusu: takip firması, radarda geçen firma, şehir ya da "Radar".</summary>
    public static string Subject(NewsRow r) =>
        !string.IsNullOrWhiteSpace(r.CompanyName) ? r.CompanyName!
        : !string.IsNullOrWhiteSpace(r.Item.MentionedCompany) ? r.Item.MentionedCompany!
        : !string.IsNullOrWhiteSpace(r.Item.City) ? r.Item.City!
        : "Radar";

    /// <summary>Şeritte "Tesla Tesla Türkiye…" gibi tekrar olmasın: başlık zaten konuyla başlıyorsa konu yazılmaz.</summary>
    public static bool TitleStartsWithSubject(string subject, string title) =>
        title.TrimStart().StartsWith(subject.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
