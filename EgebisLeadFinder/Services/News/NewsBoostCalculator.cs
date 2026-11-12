using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services.News;

/// <summary>
/// Olumlu haberden gelen puan artışı (saf mantık). Yalnızca son 30 gündeki "yatırım" kategorili haberler sayılır:
/// önem 5 → +10, önem 4 → +6, önem 3 → +3; toplam en fazla +15. Haberler eskidikçe artış kendiliğinden düşer.
/// Risk haberleri puan düşürmez (yalnızca olumlu sinyal aranır).
/// </summary>
public static class NewsBoostCalculator
{
    public const int Cap = 15;
    public const int WindowDays = 30;

    public static int Compute(IEnumerable<(string Category, int Importance, DateTime PublishedAt)> items, DateTime utcNow)
    {
        var since = utcNow.AddDays(-WindowDays);
        var sum = items
            .Where(i => i.Category == NewsCategories.Investment && i.PublishedAt >= since)
            .Sum(i => i.Importance switch { >= 5 => 10, 4 => 6, 3 => 3, _ => 0 });
        return Math.Min(Cap, sum);
    }
}
