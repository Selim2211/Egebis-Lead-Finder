using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>Bir kullanicinin secilen donemdeki arama kaynakli harcamasi (Serper kredisi, yapay zeka cagrisi).</summary>
public record UserSpendRow(int? UserId, string UserName, int Searches, int Serper, int AiCalls, DateTime? LastAt)
{
    public int Total => Serper + AiCalls;
}

/// <summary>Bir gunun (Turkiye saati) harcamasi.</summary>
public record UserSpendDay(DateOnly Date, int Searches, int Serper, int AiCalls);

/// <summary>
/// "Kullanici API harcamasi": aramalara (SearchRun) yazilan Serper kredisi ve yapay zeka cagrisi kullaniciya gore toplanir.
/// Apollo e-posta acma ve Gemini token kaydi kullaniciya bagli tutulmadigi icin burada yer almaz.
/// </summary>
public static class UserSpendReport
{
    public const int TurkeyOffsetHours = 3;

    public static List<UserSpendRow> ByUser(IEnumerable<SearchRun> runs) =>
        runs.GroupBy(r => r.UserId)
            .Select(g => new UserSpendRow(
                g.Key,
                g.Select(r => r.User?.UserName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Bilinmeyen kullanıcı",
                g.Count(), g.Sum(r => r.SerperCredits), g.Sum(r => r.AiCalls),
                g.Max(r => (DateTime?)r.StartedAt)))
            .OrderByDescending(r => r.Total).ThenBy(r => r.UserName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Donemin her gunu icin bir satir (harcamasiz gunler sifirli) doner.</summary>
    public static List<UserSpendDay> ByDay(IEnumerable<SearchRun> runs, CreditsPeriod period)
    {
        var byDate = runs
            .GroupBy(r => DateOnly.FromDateTime(r.StartedAt.AddHours(TurkeyOffsetHours)))
            .ToDictionary(g => g.Key, g => g.ToList());
        return period.Days().Select(d => byDate.TryGetValue(d, out var list)
                ? new UserSpendDay(d, list.Count, list.Sum(r => r.SerperCredits), list.Sum(r => r.AiCalls))
                : new UserSpendDay(d, 0, 0, 0))
            .ToList();
    }
}
