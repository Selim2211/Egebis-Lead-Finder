namespace EgebisLeadFinder.Services;

/// <summary>
/// Black List ekraninin arama, filtre, siralama ve sayfalamasi. Liste ekleme sirasiyla saklanir:
/// "en yeni" = en son eklenen. (Kayitlarda tarih tutulmaz; sira eklenme sirasidir.)
/// </summary>
public static class BlacklistListing
{
    public const int PageSize = 10;
    public const string Newest = "newest", Oldest = "oldest", AZ = "az", ZA = "za";
    public const string TypeDomain = "domain", TypeName = "name";

    public static (List<string> Items, int Total, int Page) Query(IReadOnlyList<string> entries, string? q, string? type, string? sort, int page)
    {
        IEnumerable<string> list = entries;

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = TurkishText.Normalize(q.Trim());
            list = list.Where(e => TurkishText.Normalize(e).Contains(needle, StringComparison.Ordinal));
        }

        if (type == TypeDomain) list = list.Where(SearchBlacklist.IsDomain);
        else if (type == TypeName) list = list.Where(e => !SearchBlacklist.IsDomain(e));

        var filtered = list.ToList();
        filtered = sort switch
        {
            Oldest => filtered,
            AZ => filtered.OrderBy(e => TurkishText.Normalize(e), StringComparer.Ordinal).ToList(),
            ZA => filtered.OrderByDescending(e => TurkishText.Normalize(e), StringComparer.Ordinal).ToList(),
            _ => Enumerable.Reverse(filtered).ToList()
        };

        var current = Models.PagerModel.Clamp(page, filtered.Count, PageSize);
        return (filtered.Skip((current - 1) * PageSize).Take(PageSize).ToList(), filtered.Count, current);
    }
}
