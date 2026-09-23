using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

public interface IWebScraperService
{
    /// <summary>
    /// Firma sitesinin ana sayfasini ve birkac aday sayfayi okur;
    /// duz metin, e-posta, telefon ve aday kisi listesi doner.
    /// Butun siteyi crawl etmez.
    /// </summary>
    Task<ScrapedSite> ScrapeAsync(string siteUrl, CancellationToken ct = default);

    /// <summary>Verilen yollardan (robots.txt'e uyarak) en fazla maxPages sayfa okur; sayfa basina duz metin.</summary>
    Task<List<ScrapedPage>> ReadPagesAsync(
        string siteUrl, IEnumerable<string> paths, int maxPages, int maxCharsPerPage, CancellationToken ct = default);

    /// <summary>Tek bir sayfanin (ör. haber makalesi) duz metnini doner; okunamazsa null.</summary>
    Task<string?> FetchPageTextAsync(string url, int maxChars, CancellationToken ct = default);
}

public record ScrapedPage(string Url, string Text);
