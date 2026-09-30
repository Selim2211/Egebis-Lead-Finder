using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>
/// NACE bazli yapay zeka sektor analizi raporu: "bu sektor bize ne kadar uygun?". Rapor
/// kaydedilir; olusturan kullanici (ve yonetici) sonradan acip disa aktarabilir.
/// </summary>
public class SectorReport
{
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Kullanici silinse de raporda adi kalsin.</summary>
    [MaxLength(150)]
    public string? UserName { get; set; }

    /// <summary>Kullanicinin yazdigi anahtar kelime ("plastik ambalaj").</summary>
    [Required, MaxLength(150)]
    public string Keyword { get; set; } = string.Empty;

    /// <summary>Analiz edilen NACE kodlari, virgulle ("22.22,22.29").</summary>
    [Required, MaxLength(300)]
    public string Codes { get; set; } = string.Empty;

    /// <summary>En uygun bulunan sektor ve puani (liste ekraninda ozet).</summary>
    [MaxLength(200)]
    public string? TopSector { get; set; }
    public int? TopScore { get; set; }

    /// <summary>Yapay zeka sonucu (SectorAiResult) JSON.</summary>
    public string ResultJson { get; set; } = "{}";
}
