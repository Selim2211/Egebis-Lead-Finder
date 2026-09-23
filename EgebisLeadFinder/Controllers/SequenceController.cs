using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Sequences;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

/// <summary>Otomatik mail dizileri: tanimlama, lead ekleme/durdurma, gonderim ve IMAP ayarlari.</summary>
public class SequenceController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly SequenceService _sequences;
    private readonly ReplyDetectionService _replies;
    private readonly ISettingsService _settings;

    public SequenceController(ApplicationDbContext db, SequenceService sequences, ReplyDetectionService replies,
        ISettingsService settings)
    {
        _db = db;
        _sequences = sequences;
        _replies = replies;
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var sequences = await _db.EmailSequences.AsNoTracking()
            .Include(s => s.Steps).ThenInclude(st => st.Template)
            .OrderByDescending(s => s.Active).ThenBy(s => s.Name)
            .ToListAsync(ct);

        var stats = await _db.LeadSequences.AsNoTracking()
            .GroupBy(s => new { s.SequenceId, s.Status })
            .Select(g => new { g.Key.SequenceId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var replied = await _db.LeadSequences.AsNoTracking()
            .Where(s => s.StopReason == "Cevap geldi")
            .GroupBy(s => s.SequenceId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var imap = await _replies.ConfigAsync(ct);
        var dayEnd = SequenceScheduler.LocalDayStartUtc(now).AddDays(1);

        var model = new SequenceIndexViewModel
        {
            Sequences = sequences,
            Stats = stats.ToDictionary(x => (x.SequenceId, x.Status), x => x.Count),
            Replied = replied,
            Templates = await _db.EmailTemplates.AsNoTracking().Where(t => t.Active).OrderBy(t => t.Id).ToListAsync(ct),
            DailyCap = await _sequences.DailyCapAsync(ct),
            SentToday = await _sequences.SentTodayAsync(now, ct),
            DueToday = await _db.LeadSequences.CountAsync(s => s.Status == LeadSequenceStatus.Active && s.NextSendAt < dayEnd, ct),
            InWindow = SequenceScheduler.IsInWindow(now),
            ImapHost = await _settings.GetAsync(SettingKeys.ImapHost, ct),
            ImapPort = await _settings.GetAsync(SettingKeys.ImapPort, ct),
            ImapUsername = await _settings.GetAsync(SettingKeys.ImapUsername, ct),
            ImapHasPassword = !string.IsNullOrWhiteSpace(await _settings.GetAsync(SettingKeys.ImapPassword, ct)),
            ImapReady = imap.IsComplete,
            ImapLastCheck = DateTime.TryParse(await _settings.GetAsync(SettingKeys.ImapLastCheck, ct), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var lc) ? lc : null,
            ImapLastError = await _settings.GetAsync(SettingKeys.ImapLastError, ct)
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return RedirectToAction(nameof(Index));

        var templates = await _db.EmailTemplates.AsNoTracking().Where(t => t.Active).OrderBy(t => t.Id).ToListAsync(ct);
        var first = templates.FirstOrDefault(t => t.Key != EmailTemplate.FollowUpKey) ?? templates.FirstOrDefault();
        var followUp = templates.FirstOrDefault(t => t.Key == EmailTemplate.FollowUpKey) ?? first;

        // Hazir iskelet: ilk mail + 3 gun sonra takip + 5 gun sonra son takip (AI ile yazilir).
        var sequence = new EmailSequence
        {
            Name = name.Trim().Length > 120 ? name.Trim()[..120] : name.Trim(),
            Steps =
            {
                new SequenceStep { Order = 0, DelayDays = 0, TemplateId = first?.Id, UseAi = true },
                new SequenceStep { Order = 1, DelayDays = 3, TemplateId = followUp?.Id, UseAi = true },
                new SequenceStep { Order = 2, DelayDays = 5, TemplateId = followUp?.Id, UseAi = true }
            }
        };
        _db.EmailSequences.Add(sequence);
        await _db.SaveChangesAsync(ct);

        TempData["SettingsSaved"] = "Dizi oluşturuldu. Adımları aşağıdan düzenleyebilirsiniz.";
        return RedirectToAction(nameof(Index), null, $"seq-{sequence.Id}");
    }

    /// <summary>Dizinin adimlarini toplu kaydeder (form: adim basina gun, sablon, AI).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSteps(int id, string name, bool active,
        List<int> delay, List<int?> template, List<string> mode, CancellationToken ct)
    {
        var sequence = await _db.EmailSequences.Include(s => s.Steps).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sequence is null) return NotFound();

        sequence.Name = string.IsNullOrWhiteSpace(name) ? sequence.Name : name.Trim();
        sequence.Active = active;
        _db.SequenceSteps.RemoveRange(sequence.Steps);

        var steps = new List<SequenceStep>();
        for (var i = 0; i < delay.Count && i < 10; i++)
        {
            var templateId = i < template.Count ? template[i] : null;
            var useAi = i < mode.Count && mode[i] == "ai";
            if (!useAi && templateId is null) continue; // bos adim
            steps.Add(new SequenceStep
            {
                Order = steps.Count,
                DelayDays = Math.Clamp(delay[i], 0, 60),
                TemplateId = templateId,
                UseAi = useAi
            });
        }
        sequence.Steps = steps;
        await _db.SaveChangesAsync(ct);

        TempData["SettingsSaved"] = $"\"{sequence.Name}\" kaydedildi ({steps.Count} adım).";
        return RedirectToAction(nameof(Index), null, $"seq-{sequence.Id}");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var sequence = await _db.EmailSequences.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sequence is null) return NotFound();

        // Gecmis korunur: diziyi silmek yerine pasife al, aktif lead'ler durur.
        sequence.Active = false;
        var active = await _db.LeadSequences.Where(s => s.SequenceId == id && s.Status == LeadSequenceStatus.Active).ToListAsync(ct);
        foreach (var run in active)
        {
            run.Status = LeadSequenceStatus.Stopped;
            run.StopReason = "Dizi pasife alındı";
            run.NextSendAt = null;
            run.FinishedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        TempData["SettingsSaved"] = $"\"{sequence.Name}\" pasife alındı; {active.Count} lead'in dizisi durdu.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Lead detayindan veya listeden (coklu secim) diziye ekleme.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enroll(int sequenceId, int[] leadIds, string? returnUrl, CancellationToken ct)
    {
        var result = await _sequences.EnrollAsync(leadIds, sequenceId, ct);

        var message = result.Added > 0
            ? $"{result.Added} lead diziye eklendi; ilk mail hafta içi 09:00-18:00 arasında gönderilecek."
            : "Hiçbir lead diziye eklenemedi.";
        if (result.Skipped.Count > 0)
            message += " Atlananlar: " + string.Join("; ", result.Skipped.Take(8)) + (result.Skipped.Count > 8 ? "…" : "");

        TempData[result.Added > 0 ? "LeadSuccess" : "LeadError"] = message;
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action("Index", "Lead")!);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stop(int leadId, string? returnUrl, CancellationToken ct)
    {
        var stopped = await _sequences.StopForLeadAsync(leadId, "Elle durduruldu", ct);
        await _db.SaveChangesAsync(ct);
        TempData["LeadSuccess"] = stopped > 0 ? "Otomatik dizi durduruldu." : "Aktif dizi yok.";
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action("Details", "Lead", new { id = leadId })!);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(int dailyCap, string? imapHost, int? imapPort, string? imapUsername,
        string? imapPassword, CancellationToken ct)
    {
        var values = new Dictionary<string, string?>
        {
            [SettingKeys.SequenceDailyCap] = Math.Clamp(dailyCap, 0, 1000).ToString(),
            [SettingKeys.ImapHost] = imapHost?.Trim(),
            [SettingKeys.ImapPort] = imapPort is > 0 ? imapPort.ToString() : null,
            [SettingKeys.ImapUsername] = imapUsername?.Trim()
        };
        // Sifre bos birakilirsa mevcut sifre korunur.
        if (!string.IsNullOrWhiteSpace(imapPassword)) values[SettingKeys.ImapPassword] = imapPassword.Trim();

        await _settings.SetManyAsync(values, ct);
        TempData["SettingsSaved"] = "Gönderim ve gelen kutusu ayarları kaydedildi.";
        return RedirectToAction(nameof(Index), null, "settings");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestImap(CancellationToken ct)
    {
        var (ok, message) = await _replies.TestAsync(ct);
        TempData[ok ? "SettingsSaved" : "SettingsError"] = message;
        return RedirectToAction(nameof(Index), null, "settings");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckNow(CancellationToken ct)
    {
        var count = await _replies.CheckAsync(ct);
        var error = await _settings.GetAsync(SettingKeys.ImapLastError, ct);
        TempData[error is null ? "SettingsSaved" : "SettingsError"] = error ?? $"Gelen kutusu kontrol edildi: {count} cevap/geri dönüş işlendi.";
        return RedirectToAction(nameof(Index), null, "settings");
    }
}
