using System.Diagnostics;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ISettingsService _settings;

    public HomeController(ApplicationDbContext db, ISettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var followUpDays = await LeadController.FollowUpDaysAsync(_settings, ct);

        // Tek gecisle firma sayimlari (huni + sinyal dagilimi).
        var company = await _db.Companies.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                // Elenen firmalar 0 puan aldigi icin puani olanlar gercek adaydir.
                Scored = g.Count(c => c.Score > 0),
                WithLead = g.Count(c => c.Leads.Any()),
                Project = g.Count(c => c.ProjectStartedAt != null),
                ToReview = g.Count(c => c.Score >= 50 && !c.Leads.Any()),
                Guclu = g.Count(c => c.RatingSignal == "Guclu"),
                Incelenmeli = g.Count(c => c.RatingSignal == "Incelenmeli"),
                Riskli = g.Count(c => c.RatingSignal == "Riskli"),
                Unrated = g.Count(c => c.RatingSignal == null || c.RatingSignal == "Bilinmiyor")
            })
            .FirstOrDefaultAsync(ct);

        var lead = await _db.Leads.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Mailed = g.Count(l => l.SentAt != null),
                Replied = g.Count(l => l.RepliedAt != null),
                EmailReady = g.Count(l => l.Status == LeadStatus.EmailHazir),
                NoEmail = g.Count(l => l.Contact != null
                    && (l.Contact.Email == null || l.Contact.Email == "")
                    && l.Contact.ApolloId != null)
            })
            .FirstOrDefaultAsync(ct);

        var model = new DashboardViewModel
        {
            TotalCompanies = company?.Total ?? 0,
            PotentialLeads = company?.Scored ?? 0,
            ToReview = company?.ToReview ?? 0,
            EmailReady = lead?.EmailReady ?? 0,
            Sent = lead?.Mailed ?? 0,

            FollowUpAfterDays = followUpDays,
            DueLeads = await LeadController.OnlyDue(_db.Leads.AsNoTracking(), followUpDays, now).CountAsync(ct),
            SequenceDueToday = await _db.LeadSequences.CountAsync(s =>
                s.Status == LeadSequenceStatus.Active
                && s.NextSendAt < EgebisLeadFinder.Services.Sequences.SequenceScheduler.LocalDayStartUtc(now).AddDays(1), ct),
            LeadsWithoutEmail = lead?.NoEmail ?? 0,
            UnresearchedCompanies = company?.Unrated ?? 0,

            Funnel = new List<FunnelStep>
            {
                new("Firma", company?.Total ?? 0, Url.Action("Index", "Company")!),
                new("Lead açıldı", company?.WithLead ?? 0, Url.Action("Index", "Lead")!),
                new("Mail atıldı", lead?.Mailed ?? 0, Url.Action("Index", "Lead", new { status = LeadStatus.Gonderildi })!),
                new("Cevap geldi", lead?.Replied ?? 0, Url.Action("Index", "Lead", new { status = LeadStatus.Ilgilendi })!),
                new("Proje başladı", company?.Project ?? 0, Url.Action("Index", "Company", new { stage = CompanyStage.Project })!)
            },

            Signals = new List<SignalSlice>
            {
                new("Güçlü", company?.Guclu ?? 0, "guclu"),
                new("İncelenmeli", company?.Incelenmeli ?? 0, "incelenmeli"),
                new("Riskli", company?.Riskli ?? 0, "riskli"),
                new("Araştırılmamış", company?.Unrated ?? 0, "none")
            },

            Activity = await BuildActivityAsync(now, ct),
            Recent = await BuildRecentAsync(ct),

            TopCompanies = await _db.Companies.AsNoTracking()
                .Where(c => c.Score > 0)
                .OrderByDescending(c => c.Score)
                .Take(8)
                .ToListAsync(ct)
        };

        return View(model);
    }

    /// <summary>Son 7 gun: gonderilen e-posta ve eklenen firma sayilari.</summary>
    private async Task<List<ActivityDay>> BuildActivityAsync(DateTime now, CancellationToken ct)
    {
        var from = now.Date.AddDays(-6);

        var mails = await _db.SentEmails.AsNoTracking()
            .Where(e => e.SentAt >= from)
            .GroupBy(e => e.SentAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var companies = await _db.Companies.AsNoTracking()
            .Where(c => c.CreatedAt >= from)
            .GroupBy(c => c.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Enumerable.Range(0, 7)
            .Select(i => from.AddDays(i))
            .Select(day => new ActivityDay(
                day,
                mails.FirstOrDefault(m => m.Day == day)?.Count ?? 0,
                companies.FirstOrDefault(c => c.Day == day)?.Count ?? 0))
            .ToList();
    }

    /// <summary>Son hareketler: gonderilen mail, acilan lead, baslayan proje.</summary>
    private async Task<List<RecentEvent>> BuildRecentAsync(CancellationToken ct)
    {
        var mails = await _db.SentEmails.AsNoTracking()
            .OrderByDescending(e => e.SentAt)
            .Take(6)
            .Select(e => new RecentEvent("mail", "E-posta gönderildi", e.ToAddress, e.SentAt,
                "/Lead/Details/" + e.LeadId))
            .ToListAsync(ct);

        var leads = await _db.Leads.AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .Take(6)
            .Select(l => new RecentEvent("lead", "Lead oluşturuldu",
                l.Contact != null && l.Contact.Name != null ? l.Contact.Name : l.Company!.Name,
                l.CreatedAt, "/Lead/Details/" + l.Id))
            .ToListAsync(ct);

        var projects = await _db.Companies.AsNoTracking()
            .Where(c => c.ProjectStartedAt != null)
            .OrderByDescending(c => c.ProjectStartedAt)
            .Take(4)
            .Select(c => new RecentEvent("project", "Proje başladı", c.Name, c.ProjectStartedAt!.Value,
                "/Company/Details/" + c.Id))
            .ToListAsync(ct);

        return mails.Concat(leads).Concat(projects)
            .OrderByDescending(e => e.At)
            .Take(8)
            .ToList();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
