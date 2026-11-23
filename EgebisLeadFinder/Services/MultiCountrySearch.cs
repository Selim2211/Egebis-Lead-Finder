using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Çok ülkeli profil (ör. kıtadan seçilen ülkeler): her ülke kendi dili ve Google ülke koduyla ayrı taranır, firma
/// hedefi ülkelere eşit paylaştırılır, aynı alan adı bir kez alınır. Bir ülkede kota/hata olursa o ana kadar
/// bulunanlarla devam edilir (kısmi sonuç). Tek ülkede doğrudan aramaya gider.
/// </summary>
public static class MultiCountrySearch
{
    public static async Task<List<SearchResult>> RunAsync(ISearchService search, SearchCriteria criteria, CancellationToken ct)
    {
        var regions = criteria.Regions();
        if (regions.Count <= 1) return await search.SearchCompaniesAsync(criteria, ct);

        var perCountry = Math.Max(1, (int)Math.Ceiling(criteria.MaxCompanies / (double)regions.Count));
        var all = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? partialError = null;

        foreach (var region in regions)
        {
            ct.ThrowIfCancellationRequested();
            List<SearchResult> hits;
            try
            {
                hits = await search.SearchCompaniesAsync(criteria.ForCountry(region, perCountry), ct);
            }
            catch (SearchProviderException ex) when (ex.Partial.Count > 0)
            {
                hits = ex.Partial;
                partialError = ex.Message;
            }
            catch (Exception ex) when (ex is SearchProviderException or QuotaExceededException)
            {
                // İlk ülkede hata: aramanın kendisi başarısız. Sonrakilerde: o ana kadar bulunanlarla devam.
                if (all.Count == 0) throw;
                throw new SearchProviderException($"{region.Name} aranırken: {ex.Message}") { Partial = all };
            }

            foreach (var hit in hits)
                if (seen.Add(hit.Domain))
                {
                    hit.Country ??= region.Country;
                    all.Add(hit);
                }
            if (partialError is not null) break;
        }

        if (partialError is not null) throw new SearchProviderException(partialError) { Partial = all };
        return all;
    }
}
