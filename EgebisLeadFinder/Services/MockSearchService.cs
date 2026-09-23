using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// API anahtari olmadan gelistirme yapabilmek icin sahte arama servisi.
/// appsettings -> Search:Provider = "Mock" oldugunda devreye girer.
/// </summary>
public class MockSearchService : ISearchService
{
    private static readonly string[] SampleDomains =
    {
        "example-otomotiv.com.tr", "ornekmakina.com", "demirdokum-ornek.com",
        "sample-metal.com.tr", "testplastik.com", "ornek-kaliphane.com",
        "deneme-yansanayi.com", "ornekpres.com.tr", "sample-doku.com",
        "ornek-elektromekanik.com"
    };

    public Task<List<SearchResult>> SearchCompaniesAsync(SearchCriteria criteria, CancellationToken ct = default)
    {
        var results = SampleDomains
            .Take(criteria.MaxCompanies)
            .Select(d => new SearchResult
            {
                Domain = d,
                Url = $"https://{d}",
                Title = DomainHelper.GuessNameFromDomain(d),
                Snippet = $"{criteria.Industry} alanında faaliyet gösteren örnek firma ({criteria.City})."
            })
            .ToList();

        return Task.FromResult(results);
    }

    /// <summary>Gelistirmede LinkedIn zenginlestirmesini de anahtarsiz denemek icin sahte sonuc.</summary>
    public Task<List<SearchResult>> SearchLinkedInProfilesAsync(string companyName, CancellationToken ct = default)
    {
        var results = new List<SearchResult>
        {
            new()
            {
                Title = $"Ahmet Yılmaz - Bilgi İşlem Müdürü - {companyName} | LinkedIn",
                Url = "https://www.linkedin.com/in/ornek-profil-1"
            },
            new()
            {
                Title = $"Ayşe Kaya - SAP Danışmanı - {companyName} | LinkedIn",
                Url = "https://www.linkedin.com/in/ornek-profil-2"
            }
        };

        return Task.FromResult(results);
    }

    /// <summary>Faz-II on arastirma: anahtarsiz gelistirme icin sahte haber sonuclari.</summary>
    public Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default)
    {
        var results = new List<SearchResult>
        {
            new()
            {
                Title = query + " - ornek haber basligi",
                Url = "https://ornek-haber.com.tr/haber/1",
                Snippet = "Firma gecen yil kapasite artisi yatirimi duyurdu; ihracat rakamlari yukseldi."
            }
        };

        return Task.FromResult(results.Take(maxResults).ToList());
    }
}
