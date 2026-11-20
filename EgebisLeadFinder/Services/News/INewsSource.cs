namespace EgebisLeadFinder.Services.News;

/// <summary>Bir kaynaktan dönen ham haber (henüz kaydedilmemiş).</summary>
/// <summary>Region: kaynağın bildirdiği bölge ("tr"/"global"); null ise adres ve başlıktan tahmin edilir.</summary>
public record NewsHit(string Title, string Url, string? Source, DateTime? PublishedUtc, string? Snippet, string? Region = null);

/// <summary>Haber kaynağı. Ana kaynak ücretsiz RSS; yedek kaynak (Serper) yalnızca ana kaynak hata verince kullanılır.</summary>
public interface INewsSource
{
    string Name { get; }

    /// <summary>Yedek kaynak mı (krediyle çalışır)? Yalnızca ana kaynak başarısız olunca denenir.</summary>
    bool IsFallback { get; }

    Task<List<NewsHit>> SearchAsync(string query, CancellationToken ct = default);
}
