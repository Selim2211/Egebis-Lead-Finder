namespace EgebisLeadFinder.Services;

/// <summary>
/// Isim + firma domaininden kurumsal e-posta bulmaya calisir. Apollo dısında
/// baska bir saglayiciya gecilirse (Hunter.io vb.) tek bu arayuz uygulanir.
/// </summary>
public interface IPersonEmailFinder
{
    Task<PersonMatchResult> MatchAsync(string firstName, string lastName, string domain, CancellationToken ct = default);

    /// <summary>
    /// Apollo kisi kimligiyle (people/match?id=) eslesme. "api_search" ucundan gelen
    /// adaylarda soyad kismen gizli oldugu icin isim yerine bu tercih edilir;
    /// tam isim de bu cagrinin cevabindan (<see cref="PersonMatchResult"/>) okunur.
    /// </summary>
    Task<PersonMatchResult> MatchByIdAsync(string apolloId, CancellationToken ct = default);
}

/// <summary>Eslesme sonucu. E-posta bulunamazsa Found=false, akis kirilmaz.</summary>
public class PersonMatchResult
{
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Error { get; init; }

    /// <summary>Id bazli eslesmede acilan tam isim (soyad artik gizli degil).</summary>
    public string? Name { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? ProfileUrl { get; init; }

    /// <summary>Mevcut isyerindeki baslama tarihi (employment_history, current=true).</summary>
    public DateOnly? EmploymentStartDate { get; init; }

    public string? Location { get; init; }
    public string? Headline { get; init; }

    public bool Found => Email is not null;

    public static PersonMatchResult NotFound(string? error = null) => new() { Error = error };
}
