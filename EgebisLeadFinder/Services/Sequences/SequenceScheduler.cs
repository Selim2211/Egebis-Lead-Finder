namespace EgebisLeadFinder.Services.Sequences;

/// <summary>
/// Otomatik dizi gonderimlerinin zamanlama kurallari. Saf mantik (DB/saat bagimsiz) ki
/// test edilebilsin: yalnizca hafta ici 09:00-18:00 (Turkiye saati) gonderilir.
/// </summary>
public static class SequenceScheduler
{
    public const int WindowStartHour = 9;
    public const int WindowEndHour = 18;

    public static readonly TimeZoneInfo Istanbul = FindTimeZone();

    /// <summary>Verilen an gonderim penceresinde mi?</summary>
    public static bool IsInWindow(DateTime utc, TimeZoneInfo? tz = null)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz ?? Istanbul);
        return IsWorkday(local) && local.Hour >= WindowStartHour && local.Hour < WindowEndHour;
    }

    /// <summary>
    /// fromUtc'den delayDays gun sonrasi; pencere disina dusuyorsa bir sonraki
    /// is gunu 09:00'a kaydirilir. delayDays = 0 ise "ilk uygun an".
    /// </summary>
    public static DateTime NextSendAt(DateTime fromUtc, int delayDays, TimeZoneInfo? tz = null)
    {
        tz ??= Istanbul;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc), tz)
            .AddDays(Math.Max(0, delayDays));
        return TimeZoneInfo.ConvertTimeToUtc(ClampToWindow(local), tz);
    }

    /// <summary>Pencere disindaki bir yerel zamani bir sonraki pencere acilisina tasir.</summary>
    public static DateTime ClampToWindow(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (local.Hour >= WindowEndHour)
            local = local.Date.AddDays(1).AddHours(WindowStartHour);
        else if (local.Hour < WindowStartHour)
            local = local.Date.AddHours(WindowStartHour);

        while (!IsWorkday(local))
            local = local.Date.AddDays(1).AddHours(WindowStartHour);

        return local;
    }

    /// <summary>Bugun (yerel) basinin UTC karsiligi: gunluk tavan sayimi icin.</summary>
    public static DateTime LocalDayStartUtc(DateTime utc, TimeZoneInfo? tz = null)
    {
        tz ??= Istanbul;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), tz);
    }

    public static string? DomainOf(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.LastIndexOf('@');
        return at < 0 ? null : email[(at + 1)..].Trim().ToLowerInvariant();
    }

    private static bool IsWorkday(DateTime d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    private static TimeZoneInfo FindTimeZone()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "Türkiye", "Türkiye");
    }
}
