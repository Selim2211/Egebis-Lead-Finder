using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services;

/// <summary>Bir kullanicinin paylasilan favori listesi (liste secicide gosterilir).</summary>
public record FavoriteListInfo(int UserId, string OwnerName, int Count, bool IsPublic);

/// <summary>
/// Kullaniciya ozel favori firma listesi. Liste kisiye ozeldir; sahibi herkese acarsa diger
/// kullanicilar salt okunur gorur (ekleyip cikaramaz).
/// </summary>
public class FavoriteService
{
    private readonly ApplicationDbContext _db;

    public FavoriteService(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// Favoriye ekler ya da cikarir; yeni durumu doner (true = favoride). Firma yoksa veya kullanici
    /// goremiyorsa (baskasinin ozel arama sonucu) null. Favoriden cikarmak her zaman serbesttir.
    /// </summary>
    public async Task<bool?> ToggleAsync(int userId, int companyId, bool isAdmin = false, CancellationToken ct = default)
    {
        var existing = await _db.FavoriteCompanies.FirstOrDefaultAsync(f => f.UserId == userId && f.CompanyId == companyId, ct);
        if (existing is not null)
        {
            _db.FavoriteCompanies.Remove(existing);
            await _db.SaveChangesAsync(ct);
            return false;
        }

        if (!await _db.Companies.VisibleTo(_db, userId, isAdmin).AnyAsync(c => c.Id == companyId, ct)) return null;

        _db.FavoriteCompanies.Add(new FavoriteCompany { UserId = userId, CompanyId = companyId });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Ayni anda iki tiklama: kayit zaten eklenmis.
        }
        return true;
    }

    /// <summary>Verilen firmalardan kullanicinin favorisinde olanlar (liste ekraninda yildizlari boyamak icin).</summary>
    public async Task<HashSet<int>> FavoriteIdsAsync(int? userId, IEnumerable<int> companyIds, CancellationToken ct = default)
    {
        if (userId is null) return new HashSet<int>();
        var ids = companyIds.Distinct().ToList();
        if (ids.Count == 0) return new HashSet<int>();

        return (await _db.FavoriteCompanies.AsNoTracking()
            .Where(f => f.UserId == userId && ids.Contains(f.CompanyId))
            .Select(f => f.CompanyId)
            .ToListAsync(ct)).ToHashSet();
    }

    public Task<bool> IsFavoriteAsync(int? userId, int companyId, CancellationToken ct = default) =>
        userId is null
            ? Task.FromResult(false)
            : _db.FavoriteCompanies.AnyAsync(f => f.UserId == userId && f.CompanyId == companyId, ct);

    /// <summary>Kullanici bu kisinin listesini gorebilir mi? Kendi listesi her zaman; baskasininki herkese aciksa.</summary>
    public async Task<bool> CanViewAsync(int? viewerId, int ownerId, CancellationToken ct = default) =>
        viewerId == ownerId || await _db.Users.AnyAsync(u => u.Id == ownerId && u.IsActive && u.FavoritesPublic, ct);

    public async Task SetPublicAsync(int userId, bool isPublic, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;
        user.FavoritesPublic = isPublic;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Liste secici: kullanicinin kendi listesi + diger kullanicilarin herkese acik listeleri.</summary>
    public async Task<List<FavoriteListInfo>> ListsAsync(int? viewerId, CancellationToken ct = default)
    {
        var rows = await _db.Users.AsNoTracking()
            .Where(u => u.Id == viewerId || (u.IsActive && u.FavoritesPublic))
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.FullName,
                u.FavoritesPublic,
                Count = _db.FavoriteCompanies.Count(f => f.UserId == u.Id)
            })
            .ToListAsync(ct);

        return rows
            .Where(r => r.Id == viewerId || r.Count > 0)
            .Select(r => new FavoriteListInfo(r.Id, string.IsNullOrWhiteSpace(r.FullName) ? r.UserName : r.FullName!, r.Count, r.FavoritesPublic))
            .OrderByDescending(r => r.UserId == viewerId)
            .ThenBy(r => r.OwnerName, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
            .ToList();
    }

    /// <summary>Bir listedeki firmalar, en son eklenen en ustte.</summary>
    public Task<List<FavoriteCompany>> EntriesAsync(int ownerId, CancellationToken ct = default) =>
        _db.FavoriteCompanies.AsNoTracking()
            .Where(f => f.UserId == ownerId)
            .Include(f => f.Company).ThenInclude(c => c.Contacts)
            .Include(f => f.Company).ThenInclude(c => c.Leads)
            .OrderByDescending(f => f.AddedAt)
            .AsSplitQuery()
            .ToListAsync(ct);
}
