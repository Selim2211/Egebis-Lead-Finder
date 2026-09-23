using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

public interface ISearchService
{
    /// <summary>
    /// Kriterlerden arama sorgulari uretir, Search API'ye sorar,
    /// tekillestirilmis ve filtrelenmis firma adayi listesi doner.
    /// </summary>
    Task<List<SearchResult>> SearchCompaniesAsync(SearchCriteria criteria, CancellationToken ct = default);

    /// <summary>
    /// Google Dorking ile firmaya bagli, herkese acik LinkedIn profil sonuclarini arar.
    /// LinkedIn'e dogrudan istek atilmaz; yalnizca Serper'in indeksledigi arama sonucu
    /// basliklari okunur. Enrichment:Enabled=false iken hic cagrilmamalidir.
    /// </summary>
    Task<List<SearchResult>> SearchLinkedInProfilesAsync(string companyName, CancellationToken ct = default);

    /// <summary>
    /// Genel amacli web aramasi. Faz-II firma on arastirmasinda (haber, itibar, risk,
    /// buyume) hedefli sorgular icin kullanilir. Sonuc: baslik + link + snippet.
    /// </summary>
    Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default);
}
