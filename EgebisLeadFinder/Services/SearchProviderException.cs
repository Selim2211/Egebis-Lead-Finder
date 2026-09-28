namespace EgebisLeadFinder.Services;

/// <summary>
/// Arama saglayicisi (Serper) tum aramayi etkileyecek bir hata dondu: kredi/kota bitti,
/// anahtar gecersiz veya hiz limiti. Tek bir sorgunun gecici hatasindan farkli olarak
/// yutulmaz; arama durdurulur ve kullaniciya aciklamasiyla gosterilir.
/// </summary>
public class SearchProviderException : InvalidOperationException
{
    public SearchProviderException(string message) : base(message) { }

    /// <summary>Hata gelmeden once bulunabilen firmalar; arama bunlarla devam edebilir.</summary>
    public List<Models.SearchResult> Partial { get; init; } = new();

    /// <summary>Serper yanitini siniflandirir; aramayi durdurmasi gerekmeyen hatalarda null doner.</summary>
    public static SearchProviderException? FromSerper(int status, string? body)
    {
        var text = (body ?? string.Empty).ToLowerInvariant();
        var credit = text.Contains("credit") || text.Contains("quota") || text.Contains("limit");

        return status switch
        {
            401 or 403 when !credit =>
                new SearchProviderException($"Arama servisi (Serper) anahtarı geçersiz veya yetkisiz (HTTP {status}). Ayarlar'daki Serper API anahtarını kontrol edin."),
            402 or 429 or 401 or 403 or 400 when credit =>
                new SearchProviderException($"Arama servisi (Serper) kredisi/kotası doldu (HTTP {status}). Kredi yükleyin veya kotanın yenilenmesini bekleyin."),
            429 =>
                new SearchProviderException("Arama servisi (Serper) çok fazla istek nedeniyle geçici olarak yanıt vermiyor (HTTP 429). Birkaç dakika sonra tekrar deneyin."),
            _ => null
        };
    }
}
