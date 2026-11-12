namespace EgebisLeadFinder.Services.News;

/// <summary>Tarama sırasındaki bir hedef: firma ya da radar konusu. LastScanned null = hiç taranmamış.</summary>
public record NewsTarget(string Kind, int RefId, DateTime? LastScanned);

/// <summary>
/// Dönüşümlü tarama sırası (saf mantık). Her tur sınırlı sayıda sorgu atılır:
///  1. hiç taranmamışlar (yeni "Takip et" ve yeni radar konusu) en önce,
///  2. süresi dolan radar konuları (en eski önce),
///  3. kalan yer: süresi dolan firmalar (en eski önce).
/// Süresi dolmamış hedef atlanır; böylece az sayıda firma olduğunda aynı firmalar 10 dakikada bir boşuna taranmaz.
/// </summary>
public static class NewsQueue
{
    public static List<NewsTarget> Pick(IEnumerable<NewsTarget> targets, DateTime utcNow, int max,
        TimeSpan topicInterval, TimeSpan companyInterval)
    {
        if (max <= 0) return new List<NewsTarget>();
        var all = targets.ToList();

        var fresh = all.Where(t => t.LastScanned is null)
            .OrderBy(t => t.Kind == NewsCursorKindsLocal.Topic ? 0 : 1).ThenBy(t => t.RefId);

        var topics = all.Where(t => t.Kind == NewsCursorKindsLocal.Topic && t.LastScanned is { } l && utcNow - l >= topicInterval)
            .OrderBy(t => t.LastScanned);

        var companies = all.Where(t => t.Kind == NewsCursorKindsLocal.Company && t.LastScanned is { } l && utcNow - l >= companyInterval)
            .OrderBy(t => t.LastScanned);

        return fresh.Concat(topics).Concat(companies).Take(max).ToList();
    }

    private static class NewsCursorKindsLocal
    {
        public const string Company = Models.NewsCursorKinds.Company;
        public const string Topic = Models.NewsCursorKinds.Topic;
    }
}
