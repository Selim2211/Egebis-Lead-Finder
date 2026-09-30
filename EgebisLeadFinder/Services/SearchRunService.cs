using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Arama gecmisi: her arama bir SearchRun olarak kaydedilir ve bulunan firmalar (yeni + zaten
/// kayitli) ona baglanir. Profil aramasinda firmalar profile de otomatik eklenir.
/// </summary>
public class SearchRunService
{
    private readonly ApplicationDbContext _db;

    public SearchRunService(ApplicationDbContext db) => _db = db;

    public async Task<SearchRun> StartAsync(SearchCriteria criteria, SearchProfile? profile, int? userId, CancellationToken ct = default)
    {
        var run = new SearchRun
        {
            UserId = userId,
            Kind = criteria.IsNameSearch ? SearchRunKind.Name : profile is not null ? SearchRunKind.Profile : SearchRunKind.Sector,
            Industry = Trim(criteria.Industry, 150),
            CompanyName = Trim(criteria.CompanyName, 150),
            RegionKey = criteria.RegionKey,
            City = string.IsNullOrWhiteSpace(criteria.City) ? null : criteria.City,
            SearchProfileId = profile?.Id,
            ProfileName = profile?.Name
        };
        _db.SearchRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        return run;
    }

    /// <summary>Arama bitti (veya iptal/yarida kaldi): sayilar yazilir, firmalar aramaya ve profile baglanir.</summary>
    public async Task CompleteAsync(int runId, DiscoveryResult result, CancellationToken ct = default)
    {
        var run = await _db.SearchRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return;

        var ids = result.Companies.Where(c => c.Id > 0).Select(c => c.Id).Distinct().ToList();
        var existing = await _db.SearchRunCompanies.Where(x => x.SearchRunId == runId).Select(x => x.CompanyId).ToListAsync(ct);
        foreach (var id in ids.Except(existing))
            _db.SearchRunCompanies.Add(new SearchRunCompany { SearchRunId = runId, CompanyId = id, IsNew = result.NewCompanyIds.Contains(id) });

        if (run.SearchProfileId is int profileId)
        {
            // Bag aramayi yapan kullaniciya aittir: herkese acik sablonu kullanan baskasi sonuclari gormez.
            var linked = await _db.CompanySearchProfiles
                .Where(x => x.SearchProfileId == profileId && x.UserId == run.UserId && ids.Contains(x.CompanyId))
                .Select(x => x.CompanyId).ToListAsync(ct);
            foreach (var id in ids.Except(linked))
                _db.CompanySearchProfiles.Add(new CompanySearchProfile { SearchProfileId = profileId, CompanyId = id, UserId = run.UserId });
        }

        run.FinishedAt = DateTime.UtcNow;
        run.Status = result.Cancelled ? SearchRunStatus.Cancelled
            : result.AbortReason is not null ? SearchRunStatus.Aborted
            : SearchRunStatus.Done;
        run.Found = result.FoundBySearch;
        run.NewCount = result.NewCompanyIds.Count;
        run.KnownCount = result.AlreadyKnown;
        run.FailedCount = result.Failed;
        run.AbortReason = Trim(result.AbortReason, 500);

        await _db.SaveChangesAsync(ct);
    }

    public async Task FailAsync(int runId, string error, CancellationToken ct = default)
    {
        var run = await _db.SearchRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return;
        run.Status = SearchRunStatus.Failed;
        run.FinishedAt = DateTime.UtcNow;
        run.AbortReason = Trim(error, 500);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Firmalar ekranindaki secici: son aramalar (sonucu olanlar), en yeni en ustte. Sonuclar kisiye
    /// ozeldir; yonetici denetim icin herkesin aramasini gorur.
    /// </summary>
    public Task<List<SearchRun>> RecentAsync(int? userId, bool isAdmin, int take = 30, CancellationToken ct = default) =>
        _db.SearchRuns.AsNoTracking()
            .VisibleTo(userId, isAdmin)
            .Include(r => r.User)
            .Where(r => r.Companies.Any())
            .OrderByDescending(r => r.StartedAt)
            .Take(take)
            .ToListAsync(ct);

    /// <summary>Varsayilan arama: kullanicinin son sonuclu aramasi; yoksa null (tum firmalar).</summary>
    public async Task<int?> DefaultRunIdAsync(int? userId, CancellationToken ct = default)
    {
        if (userId is null) return null;
        return await _db.SearchRuns.AsNoTracking()
            .Where(r => r.UserId == userId && r.Companies.Any())
            .OrderByDescending(r => r.StartedAt).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>Kullanici bu aramanin sonuclarini gorebilir mi? (kendi aramasi; yonetici hepsini)</summary>
    public Task<bool> CanViewAsync(int runId, int? userId, bool isAdmin, CancellationToken ct = default) =>
        _db.SearchRuns.AsNoTracking().VisibleTo(userId, isAdmin).AnyAsync(r => r.Id == runId, ct);

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length > max ? value[..max] : value;
    }
}
