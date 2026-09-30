using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

public enum UserRole
{
    /// <summary>Arama, firma, lead, mail ve taslak islemleri.</summary>
    User = 0,

    /// <summary>Kullanici islemleri + ayarlar, API anahtarlari, kullanicilar, audit log, toplu silme.</summary>
    Admin = 1
}

/// <summary>Uygulamaya giris yapan kullanici. Veri ortak havuzdur; rol yalnizca yetkiyi belirler.</summary>
public class AppUser
{
    public int Id { get; set; }

    /// <summary>Giris adi; kucuk harfle saklanir, benzersizdir.</summary>
    [Required, MaxLength(64)]
    public string UserName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? FullName { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    [Required, MaxLength(500)]
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;

    public bool IsActive { get; set; } = true;

    /// <summary>Sifre/rol/aktiflik degisince yenilenir; eski oturum cerezleri gecersiz olur.</summary>
    [MaxLength(64)]
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("n");

    /// <summary>Yonetici sifre sifirladiginda ilk giriste yeni sifre istenir.</summary>
    public bool MustChangePassword { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Favori firma listesi tum kullanicilara acik mi (varsayilan: kisiye ozel).</summary>
    public bool FavoritesPublic { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? UserName : FullName!;
}

/// <summary>Kim, ne zaman, ne yapti. Kullanici silinse de ad saklanir.</summary>
public class AuditLog
{
    public long Id { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;

    public int? UserId { get; set; }

    [MaxLength(64)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>Makine okunur islem kodu ("login", "company.delete", "search.start"...).</summary>
    [Required, MaxLength(64)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(40)]
    public string? EntityType { get; set; }

    [MaxLength(40)]
    public string? EntityId { get; set; }

    /// <summary>Insan okunur ozet. Sifre, API anahtari gibi gizli degerler asla yazilmaz.</summary>
    [MaxLength(1000)]
    public string? Summary { get; set; }

    [MaxLength(64)]
    public string? Ip { get; set; }

    [MaxLength(300)]
    public string? UserAgent { get; set; }

    public bool Success { get; set; } = true;
}
