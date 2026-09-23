using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>
/// Bir dış API sağlayıcısı için kullanım sayacı (ör. Serper arama kredisi).
/// Sağlayıcı bazında tek satır tutulur; yeni API anahtarı alındığında elle
/// sıfırlanır (aylık otomatik yenilenmez — Serper ücretsiz krediler tek seferliktir).
/// </summary>
public class ApiUsage
{
    public int Id { get; set; }

    /// <summary>"Serper" gibi sağlayıcı adı.</summary>
    [Required, MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Son sıfırlamadan bu yana yapılan çağrı sayısı.</summary>
    public int Count { get; set; }

    public DateTime? ResetAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Gunluk cagri sayaci. Toplam sayimin (ApiUsage) aksine gun bazinda tutulur,
/// boylece Ayarlar ekraninda gunluk/haftalik/aylik kullanim ve maliyet gosterilebilir.
/// </summary>
public class ApiUsageDaily
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Turkiye yereline gore gun (UTC+3 sabit, DST yok).</summary>
    public DateOnly Date { get; set; }

    public int Count { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
