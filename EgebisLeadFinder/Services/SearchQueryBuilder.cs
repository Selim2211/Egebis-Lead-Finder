using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Arama sorgularini kod tarafinda uretir. Bu isi AI'a sormaya gerek yok;
/// deterministik sablonlar hem ucuz hem tahmin edilebilir (dokuman bolum 4).
/// Kaliplar bolgenin diline gore secilir (bkz. SearchRegions / QueryPack):
/// Almanya'da "Hersteller", Ingiltere'de "manufacturers" aranir.
/// </summary>
public static class SearchQueryBuilder
{
    /// <summary>
    /// Google Haritalar (Places) icin sorgu uretir. Haritalar bir isletme dizinidir,
    /// web sayfasi metni degil: tirnakli tam ifade veya "üreticileri" gibi kaliplar
    /// burada ise yaramaz. Dogru kalip "ne + nerede" seklindedir.
    /// </summary>
    public static List<string> BuildPlaceQueries(SearchCriteria c) => Build(c, places: true);

    public static List<string> Build(SearchCriteria c) => Build(c, places: false);

    private static List<string> Build(SearchCriteria c, bool places)
    {
        var industry = c.Industry.Trim();
        if (string.IsNullOrWhiteSpace(industry))
            return new List<string>();

        var region = SearchRegions.Get(c.RegionKey ?? SearchRegions.ByCountry(c.Country)?.Key);
        var templates = places ? region.Pack.Places : region.Pack.Web;

        // Sehir alani virgullu gelebilir: "İzmir, Bursa, Kocaeli"
        var cities = (c.City ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (cities.Count == 0)
            cities.Add(string.Empty);

        var country = string.IsNullOrWhiteSpace(c.Country) ? region.Country : c.Country.Trim();
        var queries = new List<string>();

        foreach (var city in cities)
        {
            // Haritalar'da sehir tek basina yeterli; web aramasinda ulke de eklenir.
            var location = string.IsNullOrWhiteSpace(city)
                ? country
                : places ? city : $"{city} {country}";

            foreach (var template in templates)
                queries.Add(string.Format(template, industry, location));

            if (!places && !string.IsNullOrWhiteSpace(c.TargetPosition))
                queries.Add($"\"{c.TargetPosition}\" \"{industry}\" {location}");
        }

        return queries
            .Select(q => q.Replace("  ", " ").Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
