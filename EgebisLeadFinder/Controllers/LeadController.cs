using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

public class LeadController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ContactRevealService _reveal;
    private readonly ISettingsService _settings;

    public LeadController(ApplicationDbContext db, ContactRevealService reveal, ISettingsService settings)
    {
        _db = db;
        _reveal = reveal;
        _settings = settings;
    }

    /// <summary>Ayarlardaki "kac gun sonra takip" degeri (varsayilan 5).</summary>
    public static async Task<int> FollowUpDaysAsync(ISettingsService settings, CancellationToken ct)
    {
        var raw = await settings.GetAsync(SettingKeys.FollowUpAfterDays, ct);
        return int.TryParse(raw, out var days) && days is > 0 and < 365
            ? days
            : SettingKeys.DefaultFollowUpAfterDays;
    }

    /// <summary>
    /// Takip bekleyen lead'ler icin sorgu suzgeci. Lead.NeedsFollowUp ile ayni kurallar,
    /// ancak veritabaninda calismasi icin son temas SentAt/ContactedAt uzerinden hesaplanir.
    /// </summary>
    public static IQueryable<Lead> OnlyDue(IQueryable<Lead> query, int afterDays, DateTime now)
    {
        var threshold = now.AddDays(-afterDays);

        return query.Where(l =>
            l.RepliedAt == null
            && l.Status != LeadStatus.Ilgilendi && l.Status != LeadStatus.Ilgilenmedi
            && (l.SnoozedUntil == null || l.SnoozedUntil <= now)
            && ((l.SentAt != null && l.SentAt <= threshold)
                || (l.SentAt == null && l.ContactedAt != null && l.ContactedAt <= threshold)));
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? q, LeadStatus? status, string? contact, string? email, string? sort, bool due,
        [FromQuery(Name = "region")] string[]? regions, [FromQuery(Name = "city")] string[]? cities,
        [FromQuery(Name = "country")] string[]? countries,
        CancellationToken ct, int page = 1)
    {
        var geo = GeoFilter.From(regions, cities, countries);
        var now = DateTime.UtcNow;
        var followUpDays = await FollowUpDaysAsync(_settings, ct);
        var query = _db.Leads
            .AsNoTracking()
            .AsQueryable();

        if (geo.Countries.Count > 0)
        {
            var names = geo.CountryNames();
            query = query.Where(l => l.Company!.Country != null && names.Contains(l.Company.Country));
        }

        if (geo.Regions.Count > 0 || geo.Cities.Count > 0)
        {
            var geoCities = geo.EffectiveCities();
            query = query.Where(l => l.Company!.City != null && geoCities.Contains(l.Company.City));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(l =>
                EF.Functions.ILike(l.Company!.Name, term) ||
                (l.Contact != null && (
                    (l.Contact.Name != null && EF.Functions.ILike(l.Contact.Name, term)) ||
                    (l.Contact.Title != null && EF.Functions.ILike(l.Contact.Title, term)) ||
                    (l.Contact.Email != null && EF.Functions.ILike(l.Contact.Email, term)))));
        }

        if (status is not null)
            query = query.Where(l => l.Status == status);

        query = contact switch
        {
            "yes" => query.Where(l => l.ContactedAt != null),
            "no" => query.Where(l => l.ContactedAt == null),
            _ => query
        };

        query = email switch
        {
            "open" => query.Where(l => l.Contact != null && l.Contact.Email != null && l.Contact.Email != ""),
            "closed" => query.Where(l => l.Contact == null || l.Contact.Email == null || l.Contact.Email == ""),
            _ => query
        };

        if (due) query = OnlyDue(query, followUpDays, now);

        sort = LeadSort.Options.Any(o => o.Key == sort) ? sort! : LeadSort.Default;
        IOrderedQueryable<Lead> ordered = sort switch
        {
            "oldest" => query.OrderBy(l => l.CreatedAt),
            "score" => query.OrderByDescending(l => l.Score).ThenByDescending(l => l.CreatedAt),
            "name" => query.OrderBy(l => l.Contact == null || l.Contact.Name == null).ThenBy(l => l.Contact!.Name),
            "company" => query.OrderBy(l => l.Company!.Name).ThenByDescending(l => l.CreatedAt),
            "contacted" => query.OrderBy(l => l.ContactedAt == null).ThenByDescending(l => l.ContactedAt),
            "mailed" => query.OrderBy(l => l.SentAt == null).ThenByDescending(l => l.SentAt),
            // En uzun bekleyen once: son temasi en eski olan lead basta.
            "waiting" => query.OrderBy(l => l.SentAt == null && l.ContactedAt == null)
                .ThenBy(l => l.SentAt ?? l.ContactedAt),
            _ => query.OrderByDescending(l => l.CreatedAt)
        };

        var filteredTotal = await query.CountAsync(ct);
        page = PagerModel.Clamp(page, filteredTotal);

        var leads = await ordered
            .ThenBy(l => l.Id)
            .Skip((page - 1) * PagerModel.DefaultPageSize)
            .Take(PagerModel.DefaultPageSize)
            .Include(l => l.Company)
            .Include(l => l.Contact)
            .Include(l => l.SentEmails)
            .AsSplitQuery()
            .ToListAsync(ct);

        geo.CityCounts = await _db.Leads.AsNoTracking()
            .Where(l => l.Company!.City != null)
            .GroupBy(l => l.Company!.City!)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var countryRows = await _db.Leads.AsNoTracking()
            .Where(l => l.Company!.Country != null)
            .GroupBy(l => l.Company!.Country!)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        geo.CountryCounts = CompanyController.CountryCounts(countryRows.Select(r => ((string?)r.Key, r.Count)));

        var all = await _db.Leads.AsNoTracking()
            .Select(l => new { l.Status, Contacted = l.ContactedAt != null })
            .ToListAsync(ct);

        var leadIds = leads.Select(l => l.Id).ToList();
        var emailCounts = await _db.SentEmails.AsNoTracking()
            .Where(e => leadIds.Contains(e.LeadId))
            .GroupBy(e => e.LeadId)
            .Select(g => new { LeadId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LeadId, x => x.Count, ct);

        var model = new LeadListViewModel
        {
            Leads = leads,
            EmailCounts = emailCounts,
            Query = q,
            Status = status,
            Contact = contact,
            Email = email,
            Sort = sort,
            Geo = geo,
            Due = due,
            DueCount = await OnlyDue(_db.Leads.AsNoTracking(), followUpDays, now).CountAsync(ct),
            FollowUpAfterDays = followUpDays,
            Pager = new PagerModel { Page = page, TotalItems = filteredTotal },
            Total = all.Count,
            ContactedCount = all.Count(x => x.Contacted),
            StatusCounts = all.GroupBy(x => x.Status).ToDictionary(g => g.Key, g => g.Count())
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var lead = await _db.Leads
            .AsNoTracking()
            .Include(l => l.Company)
            .Include(l => l.Contact)
            .Include(l => l.SentEmails)
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (lead is null) return NotFound();

        var siblings = await _db.Leads
            .AsNoTracking()
            .Include(l => l.Contact)
            .Where(l => l.CompanyId == lead.CompanyId && l.Id != lead.Id)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        return View(new LeadDetailViewModel
        {
            Lead = lead,
            OtherLeads = siblings,
            FollowUpAfterDays = await FollowUpDaysAsync(_settings, ct)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, LeadStatus status, string? returnUrl, CancellationToken ct)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound();

        lead.Status = status;
        if (status == LeadStatus.Gonderildi && lead.SentAt is null)
            lead.SentAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        if (WantsJson())
            return Json(new { label = LeadStatusDisplay.Label(status), css = LeadStatusDisplay.Css(status) });

        return RedirectToLocal(returnUrl, id);
    }

    /// <summary>
    /// Lead kartindaki iki takip butonu. "contacted": kisiyle iletisim kuruldu.
    /// "mailed": e-posta atildi (durum "Mail atıldı" olur). Tekrar basmak geri alir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, string what, string? returnUrl, CancellationToken ct)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound();

        DateTime? at;
        switch (what)
        {
            case "contacted":
                lead.ContactedAt = lead.ContactedAt is null ? DateTime.UtcNow : null;
                at = lead.ContactedAt;
                break;
            case "mailed":
                if (lead.SentAt is null)
                {
                    lead.SentAt = DateTime.UtcNow;
                    if (lead.Status < LeadStatus.Gonderildi) lead.Status = LeadStatus.Gonderildi;
                }
                else
                {
                    lead.SentAt = null;
                    if (lead.Status == LeadStatus.Gonderildi) lead.Status = LeadStatus.Incelendi;
                }
                at = lead.SentAt;
                break;
            default:
                return BadRequest(new { error = "Bilinmeyen işlem." });
        }

        await _db.SaveChangesAsync(ct);

        if (WantsJson())
            return Json(new
            {
                active = at is not null,
                at = at?.ToLocalTime().ToString("dd.MM.yyyy"),
                statusLabel = LeadStatusDisplay.Label(lead.Status),
                statusCss = LeadStatusDisplay.Css(lead.Status)
            });

        return RedirectToLocal(returnUrl, id);
    }

    /// <summary>
    /// Kisiden cevap geldi: lead takip listesinden cikar ve "İlgilendi" durumuna gecer.
    /// Tekrar basmak isareti kaldirir (yanlis tiklama).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReplied(int id, string? returnUrl, CancellationToken ct)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound();

        if (lead.RepliedAt is null)
        {
            lead.RepliedAt = DateTime.UtcNow;
            lead.SnoozedUntil = null;
            if (lead.Status is not (LeadStatus.Ilgilendi or LeadStatus.Ilgilenmedi))
                lead.Status = LeadStatus.Ilgilendi;
        }
        else
        {
            lead.RepliedAt = null;
        }

        await _db.SaveChangesAsync(ct);

        TempData["LeadSuccess"] = lead.RepliedAt is null
            ? "Cevap işareti kaldırıldı."
            : "Cevap geldi olarak işaretlendi; takip listesinden çıkarıldı.";

        return RedirectToLocal(returnUrl, id);
    }

    /// <summary>Takibi erteler: lead verilen gun kadar takip listesinde gorunmez.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Snooze(int id, int days, string? returnUrl, CancellationToken ct)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound();

        days = Math.Clamp(days <= 0 ? 3 : days, 1, 90);
        lead.SnoozedUntil = DateTime.UtcNow.AddDays(days);
        await _db.SaveChangesAsync(ct);

        TempData["LeadInfo"] = $"Takip {days} gün ertelendi ({lead.SnoozedUntil.Value.ToLocalTime():dd.MM.yyyy}).";
        return RedirectToLocal(returnUrl, id);
    }

    /// <summary>Lead kisisinin e-postasini Apollo ile acar (KREDI HARCAR).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevealEmail(int id, string? returnUrl, CancellationToken ct)
    {
        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound();

        if (lead.ContactId is null)
        {
            TempData["LeadError"] = "Bu lead'e bağlı bir kişi yok.";
            return RedirectToLocal(returnUrl, id);
        }

        var outcome = await _reveal.RevealAsync(lead.ContactId.Value, ct);
        TempData[outcome.Success ? "LeadSuccess" : outcome.IsError ? "LeadError" : "LeadInfo"] = outcome.Message;

        return RedirectToLocal(returnUrl, id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNotes(int id, string? notes, CancellationToken ct)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound();

        lead.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        StringLengthGuard.Apply(lead);
        await _db.SaveChangesAsync(ct);

        TempData["LeadSuccess"] = "Not kaydedildi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Lead'i siler; kisi ve firma kaydi yerinde kalir.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound();

        _db.Leads.Remove(lead);
        await _db.SaveChangesAsync(ct);

        TempData["LeadSuccess"] = "Lead silindi.";
        return RedirectToAction(nameof(Index));
    }

    private bool WantsJson() =>
        Request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

    private IActionResult RedirectToLocal(string? returnUrl, int id) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction(nameof(Details), new { id });
}
