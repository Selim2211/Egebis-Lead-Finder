using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services.Sequences;

public record EnrollResult(int Added, List<string> Skipped);

/// <summary>
/// Otomatik mail dizileri: lead'i diziye ekler/durdurur ve zamani gelen adimlari gonderir.
/// Guvenlik kurallari: yalniz hafta ici 09-18, gunluk tavan, ayni alan adina gunde bir mail,
/// cevap gelen / gecersiz e-postali / "ilgilenmedi" lead'e gonderim yok.
/// </summary>
public class SequenceService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _sender;
    private readonly EmailTemplateService _templates;
    private readonly EmailDraftService _drafts;
    private readonly ISettingsService _settings;
    private readonly ILogger<SequenceService> _logger;

    public SequenceService(ApplicationDbContext db, IEmailSender sender, EmailTemplateService templates,
        EmailDraftService drafts, ISettingsService settings, ILogger<SequenceService> logger)
    {
        _db = db;
        _sender = sender;
        _templates = templates;
        _drafts = drafts;
        _settings = settings;
        _logger = logger;
    }

    public async Task<EnrollResult> EnrollAsync(IEnumerable<int> leadIds, int sequenceId, CancellationToken ct = default)
    {
        var sequence = await _db.EmailSequences.Include(s => s.Steps)
            .FirstOrDefaultAsync(s => s.Id == sequenceId && s.Active, ct);
        var skipped = new List<string>();
        if (sequence is null || sequence.Steps.Count == 0)
            return new EnrollResult(0, new List<string> { "Dizi bulunamadı, pasif veya adımı yok." });

        var ids = leadIds.Distinct().ToList();
        var leads = await _db.Leads.Include(l => l.Company).Include(l => l.Contact)
            .Where(l => ids.Contains(l.Id)).ToListAsync(ct);
        var busy = await _db.LeadSequences
            .Where(s => ids.Contains(s.LeadId) && s.Status == LeadSequenceStatus.Active)
            .Select(s => s.LeadId).ToListAsync(ct);

        var first = sequence.Steps.OrderBy(s => s.Order).First();
        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var lead in leads)
        {
            var name = lead.Company?.Name ?? $"Lead #{lead.Id}";
            var reason = SkipReason(lead, busy.Contains(lead.Id));
            if (reason is not null)
            {
                skipped.Add($"{name}: {reason}");
                continue;
            }

            _db.LeadSequences.Add(new LeadSequence
            {
                LeadId = lead.Id,
                SequenceId = sequence.Id,
                CurrentStep = first.Order,
                NextSendAt = SequenceScheduler.NextSendAt(now, first.DelayDays),
                StartedAt = now
            });
            added++;
        }

        await _db.SaveChangesAsync(ct);
        return new EnrollResult(added, skipped);
    }

    public static string? SkipReason(Lead lead, bool alreadyActive) =>
        alreadyActive ? "zaten bir dizide"
        : lead.RepliedAt is not null ? "cevap vermiş"
        : lead.Status == LeadStatus.Ilgilenmedi ? "ilgilenmedi olarak işaretli"
        : string.IsNullOrWhiteSpace(lead.Contact?.Email) ? "e-posta adresi yok"
        : lead.Contact.EmailStatus == EmailStatus.Invalid ? "e-posta geçersiz"
        : null;

    /// <summary>Lead'in aktif dizilerini durdurur (kaydetmez).</summary>
    public async Task<int> StopForLeadAsync(int leadId, string reason, CancellationToken ct = default)
    {
        var active = await _db.LeadSequences
            .Where(s => s.LeadId == leadId && s.Status == LeadSequenceStatus.Active).ToListAsync(ct);
        foreach (var s in active) Stop(s, reason);
        return active.Count;
    }

    private static void Stop(LeadSequence s, string reason)
    {
        s.Status = LeadSequenceStatus.Stopped;
        s.StopReason = reason.Length > 200 ? reason[..200] : reason;
        s.NextSendAt = null;
        s.FinishedAt = DateTime.UtcNow;
    }

    public async Task<int> DailyCapAsync(CancellationToken ct = default)
    {
        var raw = await _settings.GetAsync(SettingKeys.SequenceDailyCap, ct);
        return int.TryParse(raw, out var n) && n is >= 0 and <= 1000 ? n : SettingKeys.DefaultSequenceDailyCap;
    }

    /// <summary>Bugun (Turkiye saati) diziden giden mail sayisi.</summary>
    public Task<int> SentTodayAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var dayStart = SequenceScheduler.LocalDayStartUtc(nowUtc);
        return _db.SentEmails.CountAsync(e => e.SequenceStepId != null && e.SentAt >= dayStart, ct);
    }

    /// <summary>Zamani gelen adimlari gonderir. Donen deger: gonderilen mail sayisi.</summary>
    public async Task<int> ProcessDueAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        if (!SequenceScheduler.IsInWindow(nowUtc)) return 0;

        var smtp = await _sender.GetSettingsAsync(ct);
        if (!smtp.IsConfigured) return 0;

        var remaining = await DailyCapAsync(ct) - await SentTodayAsync(nowUtc, ct);
        if (remaining <= 0) return 0;

        var due = await _db.LeadSequences
            .Include(s => s.Sequence!).ThenInclude(q => q.Steps).ThenInclude(st => st.Template)
            .Include(s => s.Lead!).ThenInclude(l => l.Contact)
            .Include(s => s.Lead!).ThenInclude(l => l.Company)
            .Where(s => s.Status == LeadSequenceStatus.Active && s.NextSendAt != null && s.NextSendAt <= nowUtc)
            .OrderBy(s => s.NextSendAt)
            .Take(remaining * 2)
            .ToListAsync(ct);

        var dayStart = SequenceScheduler.LocalDayStartUtc(nowUtc);
        var domainsToday = (await _db.SentEmails
                .Where(e => e.SentAt >= dayStart && e.Method == SentEmailMethod.Smtp)
                .Select(e => e.ToAddress).ToListAsync(ct))
            .Select(SequenceScheduler.DomainOf).Where(d => d is not null).ToHashSet()!;

        var sent = 0;
        foreach (var run in due)
        {
            if (sent >= remaining) break;
            var lead = run.Lead!;

            if (!run.Sequence!.Active) { Stop(run, "Dizi pasife alındı"); continue; }
            var reason = SkipReason(lead, alreadyActive: false);
            if (reason is not null) { Stop(run, char.ToUpper(reason[0]) + reason[1..]); continue; }

            var step = run.Sequence.Steps.OrderBy(s => s.Order).FirstOrDefault(s => s.Order >= run.CurrentStep);
            if (step is null) { Complete(run); continue; }

            var domain = SequenceScheduler.DomainOf(lead.Contact!.Email);
            if (domain is not null && domainsToday.Contains(domain))
            {
                // Ayni firmaya gunde tek mail: yarin ilk pencerede tekrar dene.
                run.NextSendAt = SequenceScheduler.NextSendAt(nowUtc, 1);
                continue;
            }

            var message = await ComposeAsync(lead, step, ct);
            if (message is null)
            {
                Stop(run, "Mail metni üretilemedi (şablon yok ve AI başarısız)");
                continue;
            }

            var images = await LoadImagesAsync(message.Value.Html, ct);
            var result = await _sender.SendAsync(lead.Contact.Email!, message.Value.Subject, message.Value.Body,
                message.Value.Html, images, ct);

            if (!result.Sent)
            {
                // SMTP gecici olarak dusmus olabilir: 1 saat sonra tekrar; kalici hata cevap/bounce ile durur.
                _logger.LogWarning("Dizi maili gönderilemedi (lead {Lead}): {Error}", lead.Id, result.Error);
                run.NextSendAt = SequenceScheduler.NextSendAt(nowUtc.AddHours(1), 0);
                continue;
            }

            SentEmailRecorder.Add(_db, lead, step.Template, result.FromAddress, lead.Contact.Email!,
                message.Value.Subject, message.Value.Body, message.Value.Html,
                images.Count == 0 ? null : string.Join(", ", images.Select(i => i.Name)),
                SentEmailMethod.Smtp, result.MessageId, step.Id);
            if (domain is not null) domainsToday.Add(domain);
            sent++;

            var next = run.Sequence.Steps.OrderBy(s => s.Order).FirstOrDefault(s => s.Order > step.Order);
            if (next is null) Complete(run);
            else
            {
                run.CurrentStep = next.Order;
                run.NextSendAt = SequenceScheduler.NextSendAt(nowUtc, next.DelayDays);
            }

            await _db.SaveChangesAsync(ct);
        }

        await _db.SaveChangesAsync(ct);
        if (sent > 0) _logger.LogInformation("Otomatik dizi: {Count} mail gönderildi.", sent);
        return sent;
    }

    private static void Complete(LeadSequence run)
    {
        run.Status = LeadSequenceStatus.Completed;
        run.NextSendAt = null;
        run.FinishedAt = DateTime.UtcNow;
    }

    /// <summary>AI adimi: gonderim aninda kisiye ozel yazilir, basarisizsa sablona duser.</summary>
    private async Task<(string Subject, string Body, string? Html)?> ComposeAsync(Lead lead, SequenceStep step, CancellationToken ct)
    {
        if (step.UseAi)
        {
            var draft = await _drafts.DraftAsync(lead.Id, step.TemplateId, followUp: step.Order > 0, ct);
            if (draft.Success)
                return (draft.Subject!, TextExtractor.ToPlainText(draft.BodyHtml!), draft.BodyHtml);
            _logger.LogWarning("Dizi AI taslağı başarısız (lead {Lead}): {Error}", lead.Id, draft.Error);
        }

        if (step.Template is null) return null;

        var (subject, body) = _templates.Render(step.Template, lead.Company!, lead.Contact);
        var html = EmailHtml.Sanitize(_templates.RenderHtml(step.Template, lead.Company!, lead.Contact));
        return (subject, body, string.IsNullOrEmpty(html) ? null : html);
    }

    private async Task<List<EmailImage>> LoadImagesAsync(string? html, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(html)) return new();
        var ids = EmailHtml.ImageIds(html);
        if (ids.Count == 0) return new();
        var images = await _db.EmailImages.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        return images.OrderBy(i => ids.IndexOf(i.Id)).ToList();
    }
}

/// <summary>5 dakikada bir zamani gelen dizi adimlarini gonderir.</summary>
public class SequenceSenderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SequenceSenderWorker> _logger;

    public SequenceSenderWorker(IServiceScopeFactory scopes, ILogger<SequenceSenderWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SequenceService>().ProcessDueAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Otomatik dizi turu başarısız.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
