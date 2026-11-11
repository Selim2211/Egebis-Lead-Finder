using System.Globalization;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>Kullanilan Krediler ekraninin donemi: gun, hafta (Pazartesi-Pazar) veya ay.</summary>
public record CreditsPeriod(string Kind, DateOnly From, DateOnly To, DateOnly Prev, DateOnly Next, string Label)
{
    public const string Day = "day", Week = "week", Month = "month";

    private static readonly CultureInfo Tr = new("tr-TR");

    /// <summary>kind: day | week | month (gecersizse week). anchor: donemin icinde herhangi bir gun.</summary>
    public static CreditsPeriod Resolve(string? kind, DateOnly anchor)
    {
        switch (kind)
        {
            case Day:
                return new CreditsPeriod(Day, anchor, anchor, anchor.AddDays(-1), anchor.AddDays(1),
                    anchor.ToString("d MMMM yyyy, dddd", Tr));
            case Month:
            {
                var from = new DateOnly(anchor.Year, anchor.Month, 1);
                var to = from.AddMonths(1).AddDays(-1);
                return new CreditsPeriod(Month, from, to, from.AddMonths(-1), from.AddMonths(1), from.ToString("MMMM yyyy", Tr));
            }
            default:
            {
                // Hafta Pazartesi baslar (DayOfWeek: Pazar = 0).
                var offset = ((int)anchor.DayOfWeek + 6) % 7;
                var from = anchor.AddDays(-offset);
                var to = from.AddDays(6);
                return new CreditsPeriod(Week, from, to, from.AddDays(-7), from.AddDays(7),
                    $"{from.ToString("d MMMM", Tr)} – {to.ToString("d MMMM yyyy", Tr)}");
            }
        }
    }

    /// <summary>
    /// Donemin veritabani (UTC) araligi: Turkiye gunu UTC+3 oldugundan 00:00 yerel = bir onceki gun 21:00 UTC.
    /// Npgsql yalnizca Kind=Utc kabul ettigi icin tarihler acikca UTC isaretlenir. Bitis hariçtir.
    /// </summary>
    public (DateTime FromUtc, DateTime ToUtc) UtcRange() => (
        DateTime.SpecifyKind(From.ToDateTime(TimeOnly.MinValue).AddHours(-3), DateTimeKind.Utc),
        DateTime.SpecifyKind(To.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-3), DateTimeKind.Utc));

    public IEnumerable<DateOnly> Days()
    {
        for (var d = From; d <= To; d = d.AddDays(1)) yield return d;
    }
}

/// <summary>Bir gunun harcamasi.</summary>
public record CreditsDay(DateOnly Date, int Serper, int Apollo, int Gemini, decimal GeminiTry, int OtherAi)
{
    public bool IsEmpty => Serper == 0 && Apollo == 0 && Gemini == 0 && OtherAi == 0;
}

/// <summary>Secilen donemin harcamasi: gun gun satirlar ve toplam.</summary>
public class CreditsReport
{
    public required CreditsPeriod Period { get; init; }
    public List<CreditsDay> Days { get; init; } = new();

    public int Serper => Days.Sum(d => d.Serper);
    public int Apollo => Days.Sum(d => d.Apollo);
    public int Gemini => Days.Sum(d => d.Gemini);
    public decimal GeminiTry => Days.Sum(d => d.GeminiTry);
    public int OtherAi => Days.Sum(d => d.OtherAi);

    /// <summary>Gunluk kayitlardan (saglayici, gun, sayi) donemin satirlarini kurar. Kayitsiz gun sifir gorunur.</summary>
    public static CreditsReport Build(CreditsPeriod period, IEnumerable<ApiUsageDaily> rows, Func<DateOnly, int, decimal> geminiCost)
    {
        var byDay = rows.Where(r => r.Date >= period.From && r.Date <= period.To).ToLookup(r => r.Date);
        var days = new List<CreditsDay>();
        foreach (var date in period.Days())
        {
            var list = byDay[date].ToList();
            int Sum(Func<ApiUsageDaily, bool> pick) => list.Where(pick).Sum(r => r.Count);

            var gemini = Sum(r => Is(r.Provider, GeminiAiService.UsageProvider));
            days.Add(new CreditsDay(
                date,
                Sum(r => Is(r.Provider, SerperSearchService.UsageProvider)),
                // Apollo'da "kredi" e-posta acma kredisidir (ApolloCredit); kisi aramasi (Apollo) kredi harcamaz.
                Sum(r => Is(r.Provider, ApolloPersonEmailFinder.CreditUsageProvider)),
                gemini,
                gemini == 0 ? 0 : geminiCost(date, gemini),
                Sum(r => !UsageScope.IsNonAi(r.Provider) && !Is(r.Provider, GeminiAiService.UsageProvider))));
        }
        return new CreditsReport { Period = period, Days = days };
    }

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
