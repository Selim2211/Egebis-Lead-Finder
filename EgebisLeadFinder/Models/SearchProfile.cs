using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>
/// Kullanicinin isimlendirip kaydettigi bir arama tanimi (sektor + iller + ulke).
/// Firma Ara'da "Gelismis Ayarlar" altinda profil adi girilince olusturulur/yeniden
/// kullanilir; Firmalar ekraninda bu profile eklenmis firmalari filtrelemek icin kullanilir.
/// </summary>
public class SearchProfile
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public string? Industry { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    /// <summary>Arama bolgesi anahtari (bkz. SearchRegions); eski kayitlarda bos olabilir.</summary>
    [MaxLength(8)]
    public string? RegionKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<CompanySearchProfile> CompanyLinks { get; set; } = new();
}

/// <summary>
/// Firma <-> arama profili baglantisi (cok-cok). Bir arama sonucunda kullanici
/// hangi firmalari bu profilin listesine eklemek istedigini secip kaydeder.
/// </summary>
public class CompanySearchProfile
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int SearchProfileId { get; set; }
    public SearchProfile SearchProfile { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
