using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>Kisinin nereden bulundugu. Seffaflik ve olasi KVKK talepleri icin ayirt edilir.</summary>
public enum ContactSource
{
    /// <summary>Firmanin kendi web sitesinden (PersonExtractor).</summary>
    Website = 0,

    /// <summary>LinkedIn arama sonucu + Apollo ile dogrulanan kurumsal e-posta.</summary>
    LinkedInApollo = 1,

    /// <summary>Apollo kisi aramasi: unvan yapisal ve tam gelir, tercih edilen kaynak.</summary>
    ApolloSearch = 2
}

/// <summary>Firma web sitesinden veya zenginlestirme akisindan cikarilan yetkili kisi.</summary>
public class Contact
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? SourceUrl { get; set; }

    /// <summary>Unvan bazli oncelik puani. Yuksek olan Egebis icin daha uygun muhatap.</summary>
    public int TitleScore { get; set; }

    public ContactSource Source { get; set; } = ContactSource.Website;

    /// <summary>
    /// Apollo kisi kimligi ("api_search" ucundan gelir). Doluysa ve Email bossa,
    /// bu kisi henuz "e-postasi acilmamis aday" demektir: kullanici "E-postayı Aç"
    /// butonuna basana kadar hicbir kredi harcanmaz (bkz. CompanyController.RevealContactEmail).
    /// </summary>
    [MaxLength(64)]
    public string? ApolloId { get; set; }

    // --- E-posta acildiginda (Apollo people/match) gelen ek profil bilgisi.
    // Kredisiz listelemede bos kalir. ---

    /// <summary>Mevcut firmadaki gorevine baslama tarihi (calisma suresi bundan hesaplanir).</summary>
    public DateOnly? EmploymentStartDate { get; set; }

    [MaxLength(150)]
    public string? Location { get; set; }

    [MaxLength(300)]
    public string? Headline { get; set; }

    /// <summary>"3 yıl 2 ay" gibi okunur calisma suresi; baslama tarihi yoksa null.</summary>
    public string? TenureText(DateOnly? today = null)
    {
        if (EmploymentStartDate is not { } start) return null;

        var now = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var months = (now.Year - start.Year) * 12 + now.Month - start.Month;
        if (now.Day < start.Day) months--;
        if (months < 0) return null;

        var years = months / 12;
        var rest = months % 12;
        return (years, rest) switch
        {
            (0, 0) => "1 aydan az",
            (0, _) => $"{rest} ay",
            (_, 0) => $"{years} yıl",
            _ => $"{years} yıl {rest} ay"
        };
    }
}
