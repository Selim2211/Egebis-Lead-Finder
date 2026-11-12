namespace EgebisLeadFinder.Services.News;

/// <summary>Türkiye günü yardımcıları (Türkiye UTC+3). Günlük sayaçlar ve tavanlar Türkiye gününe göre tutulur.</summary>
public static class NewsSlots
{
    public const int TurkeyOffsetHours = 3;

    /// <summary>Türkiye gününün başlangıcı (UTC olarak).</summary>
    public static DateTime TodayStartUtc(DateTime utcNow) =>
        DateTime.SpecifyKind(utcNow.AddHours(TurkeyOffsetHours).Date.AddHours(-TurkeyOffsetHours), DateTimeKind.Utc);
}
