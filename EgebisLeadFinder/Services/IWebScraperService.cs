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
}
