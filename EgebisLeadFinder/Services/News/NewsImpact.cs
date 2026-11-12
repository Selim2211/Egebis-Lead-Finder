using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services.News;

/// <summary>Bir haberin itibara etkisi (yatırım +önem, risk −önem, diğerleri 0) ve firma bazında hareket özeti.</summary>
public static class NewsImpact
{
    public static int Of(string category, int importance) => NewsCategories.Sentiment(category) switch
    {
        NewsCategories.Positive => importance,
        NewsCategories.Negative => -importance,
        _ => 0
    };

    /// <summary>Son N Türkiye gününün etki toplamları (en eski başta, bugün sonda).</summary>
    public static int[] Daily(IEnumerable<(DateTime PublishedUtc, int Impact)> items, DateTime nowUtc, int days = 7)
    {
        var result = new int[days];
        var todayStart = NewsSlots.TodayStartUtc(nowUtc);
        foreach (var (d, impact) in items)
        {
            var back = d >= todayStart ? 0 : (int)Math.Ceiling((todayStart - d).TotalDays);
            var idx = days - 1 - back;
            if (idx >= 0 && idx < days) result[idx] += impact;
        }
        return result;
    }

    /// <summary>Gün sonu itibar değerleri: son gün bugünkü puandır, önceki günler sonraki etkiler çıkarılarak bulunur.</summary>
    public static int[] Series(int current, int[] daily)
    {
        var s = new int[daily.Length];
        var value = current;
        for (var i = daily.Length - 1; i >= 0; i--)
        {
            s[i] = value;
            value -= daily[i];
        }
        return s;
    }

    /// <summary>Haberleri firmaya (takip) ya da adı geçen firmaya (radar) göre gruplar.</summary>
    public static List<CompanyMover> Movers(IEnumerable<MoverNews> news, DateTime nowUtc)
    {
        var todayStart = NewsSlots.TodayStartUtc(nowUtc);
        return news
            .Where(n => !string.IsNullOrWhiteSpace(n.Name))
            .GroupBy(n => n.CompanyId is int id ? "c" + id : "r" + n.Name.Trim().ToLower(new System.Globalization.CultureInfo("tr-TR")))
            .Select(g =>
            {
                var list = g.OrderByDescending(n => n.PublishedAt).ToList();
                var first = list[0];
                var daily = Daily(list.Select(n => (n.PublishedAt, Of(n.Category, n.Importance))), nowUtc);
                var withSummary = list.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n.Summary) && n.Summary != n.Title);
                var pos = list.FirstOrDefault(n => NewsCategories.Sentiment(n.Category) == NewsCategories.Positive);
                var neg = list.FirstOrDefault(n => NewsCategories.Sentiment(n.Category) == NewsCategories.Negative);
                return new CompanyMover(first.CompanyId, first.Name.Trim(),
                    first.CompanyId is null ? (string.IsNullOrWhiteSpace(first.City) ? "Radar" : "Radar · " + first.City) : first.Industry,
                    first.Score, first.Boost,
                    daily[^1], daily.Sum(), daily,
                    list.Count(n => n.PublishedAt >= todayStart), list.Count,
                    withSummary?.Summary,
                    pos is null ? null : new NewsTop(pos.Title, pos.Url, pos.Importance),
                    neg is null ? null : new NewsTop(neg.Title, neg.Url, neg.Importance),
                    new NewsTop(first.Title, first.Url, first.Importance));
            })
            .OrderByDescending(m => m.WeekImpact).ThenByDescending(m => m.Count7).ThenByDescending(m => m.Score ?? 0)
            .ToList();
    }
}
