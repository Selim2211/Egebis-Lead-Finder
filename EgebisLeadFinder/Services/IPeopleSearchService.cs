namespace EgebisLeadFinder.Services;

/// <summary>
/// Firma domaininden dogrudan karar verici arar. LinkedIn'i Google uzerinden
/// taramanin aksine yapisal veri doner: unvan tam ve kirpilmamis gelir.
/// </summary>
public interface IPeopleSearchService
{
    /// <summary>
    /// Verilen domaindeki, unvani anahtar kelimelerden biriyle eslesen kisileri arar.
    /// Saglayici yapilandirilmamissa veya sonuc yoksa bos liste doner; akis kirilmaz.
    /// </summary>
    Task<PeopleSearchResult> SearchAsync(
        string domain,
        IReadOnlyList<string> titleKeywords,
        int maxResults,
        CancellationToken ct = default);
}

/// <summary>Kisi aramasinin sonucu. Hata olsa da akis durmaz, Error alanina yazilir.</summary>
public class PeopleSearchResult
{
    public List<PersonCandidate> People { get; init; } = new();
    public string? Error { get; init; }

    /// <summary>
    /// Saglayici hic denenmedi mi (anahtar yok, kapali)? Bu durumda yedek akisa
    /// gecmek dogrudur; gercek bir "kisi bulunamadi" cevabi degildir.
    /// </summary>
    public bool Skipped { get; init; }

    public static PeopleSearchResult Empty => new();
    public static PeopleSearchResult NotConfigured(string reason) => new() { Skipped = true, Error = reason };
    public static PeopleSearchResult Failed(string error) => new() { Error = error };
}

/// <summary>
/// Kisi aramasindan donen aday. "api_search" ucu isim/e-posta acmaz (soyad
/// kismen gizli, "Ak***y" gibi); e-posta acma adiminda <see cref="ApolloId"/>
/// ile ikinci bir istek (people/match) atilir, tam isim de oradan gelir.
/// </summary>
public class PersonCandidate
{
    public string Name { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? ProfileUrl { get; init; }

    /// <summary>Apollo ad/soyad ayrik dondugu icin e-posta acma adiminda kullanilir.</summary>
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    /// <summary>
    /// Apollo kisi kimligi. "api_search" ucundan gelir; e-posta acma adiminda
    /// isim yerine bu id ile people/match cagirmak daha guvenilirdir (soyad gizli).
    /// </summary>
    public string? ApolloId { get; init; }
}
