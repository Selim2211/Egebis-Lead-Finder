using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Data;

/// <summary>
/// Kullanici hangi firmalari gorebilir? Arama sonuclari kisiye ozeldir. Yonetici tum havuzu gorur;
/// kullanici su firmalari gorur:
/// - kendi aramalarinda bulunanlar,
/// - hicbir aramaya bagli olmayan eski kayitlar,
/// - kendi favorileri,
/// - herkese acik favori listelerindekiler (paylasilan liste, firmayi da paylasir).
/// Firmalar listesi, karsilastirma ve favoriye ekleme ayni kurali kullanir.
/// </summary>
public static class CompanyVisibility
{
    public static IQueryable<Company> VisibleTo(this IQueryable<Company> query, ApplicationDbContext db, int? userId, bool isAdmin)
    {
        if (isAdmin) return query;
        return query.Where(c =>
            db.SearchRunCompanies.Any(x => x.CompanyId == c.Id && x.SearchRun.UserId == userId)
            || !db.SearchRunCompanies.Any(x => x.CompanyId == c.Id)
            || db.FavoriteCompanies.Any(f => f.CompanyId == c.Id
                && (f.UserId == userId || (f.User.FavoritesPublic && f.User.IsActive))));
    }
}
