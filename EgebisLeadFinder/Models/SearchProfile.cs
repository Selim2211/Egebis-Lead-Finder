using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>
/// Kullanicinin isimlendirip kaydettigi bir arama tanimi (sektor + iller + ulke).
/// Firma Ara'da "Gelismis Ayarlar" altinda profil adi girilince olusturulur/yeniden
/// kullanilir; Firmalar ekraninda bu profile eklenmis firmalari filtrelemek icin kullanilir.
/// Sablon varsayilan olarak sahibine ozeldir; sahibi isterse herkese acar (IsPublic).
/// Herkese acik sablonu baskasi kullanabilir ama sonuclar (firma baglari) yine kisiye ozeldir.
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

    /// <summary>Sablonu olusturan kullanici. Kullanicilar eklenmeden once kaydedilenlerde bos.</summary>
    public int? OwnerUserId { get; set; }
    public AppUser? Owner { get; set; }

    /// <summary>Tum kullanicilar gorebilir ve kullanabilir (sonuclar paylasilmaz).</summary>
    public bool IsPublic { get; set; }

    /// <summary>Sahibi duzenler/siler; sahipsiz eski sablonlari yonetici yonetir.</summary>
    public bool CanEdit(int? userId, bool isAdmin) =>
        OwnerUserId is null ? isAdmin : OwnerUserId == userId;

    public bool IsVisibleTo(int? userId) => IsPublic || OwnerUserId is null || OwnerUserId == userId;

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

    /// <summary>Bagi kuran kullanici: ayni sablonu kullanan herkes yalnizca kendi sonuclarini gorur.</summary>
    public int? UserId { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Kim hangi sablonu ve hangi arama sonucunu gorebilir?</summary>
public static class SearchVisibility
{
    /// <summary>Kendi sablonlari + herkese acik olanlar + sahipsiz eski sablonlar.</summary>
    public static IQueryable<SearchProfile> VisibleTo(this IQueryable<SearchProfile> query, int? userId) =>
        query.Where(p => p.IsPublic || p.OwnerUserId == null || p.OwnerUserId == userId);

    /// <summary>Arama sonuclari kisiye ozel; yonetici denetim icin hepsini gorur.</summary>
    public static IQueryable<SearchRun> VisibleTo(this IQueryable<SearchRun> query, int? userId, bool isAdmin) =>
        isAdmin ? query : query.Where(r => r.UserId == userId);
}
