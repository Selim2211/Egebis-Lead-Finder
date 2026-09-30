namespace EgebisLeadFinder.Models;

/// <summary>
/// Kullanicinin favori firma listesindeki bir firma. Her kullanicinin tek bir listesi vardir;
/// liste varsayilan olarak kisiye ozeldir, sahibi isterse herkese acar (AppUser.FavoritesPublic).
/// </summary>
public class FavoriteCompany
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Yildiz dugmesi: firma kartinda, arama sonucunda ve detayda ayni gorunum.</summary>
public record FavoriteStar(int CompanyId, bool IsFavorite, bool WithLabel = false);
