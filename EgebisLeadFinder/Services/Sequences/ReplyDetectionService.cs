using System.Text.RegularExpressions;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace EgebisLeadFinder.Services.Sequences;

public enum InboundKind { Reply, AutoReply, Bounce }

/// <summary>Gelen mailin siniflandirmaya yetecek ozeti (MailKit'ten bagimsiz, test edilebilir).</summary>
public record InboundMessage(
    string? FromAddress,
    string? Subject,
    DateTime Date,
    IReadOnlyList<string> ReferencedMessageIds,
    string? AutoSubmitted,
    string? BodyText);

/// <summary>Gelen mail cevap mi, otomatik yanit mi, geri donen mail mi; hangi adrese ait?</summary>
public static class ReplyMatcher
{
    private static readonly string[] BounceSenders = { "mailer-daemon", "postmaster", "mail delivery" };

    private static readonly string[] BounceSubjects =
    {
        "undeliverable", "undelivered", "delivery status notification", "mail delivery failed",
        "returned mail", "delivery failure", "iletilemedi", "teslim edilemedi", "failure notice"
    };

    private static readonly string[] AutoReplySubjects =
    {
        "out of office", "automatic reply", "auto reply", "autoreply", "otomatik yanıt", "otomatik cevap",
        "ofis dışında", "izindeyim", "abwesenheit", "automatische antwort"
    };

    private static readonly Regex EmailRegex = new(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static InboundKind Classify(InboundMessage m)
    {
        var from = (m.FromAddress ?? string.Empty).ToLowerInvariant();
        var subject = (m.Subject ?? string.Empty).ToLowerInvariant();

        if (BounceSenders.Any(from.Contains) || BounceSubjects.Any(subject.Contains)) return InboundKind.Bounce;

        if (!string.IsNullOrWhiteSpace(m.AutoSubmitted) && !m.AutoSubmitted.Equals("no", StringComparison.OrdinalIgnoreCase))
            return InboundKind.AutoReply;
        if (AutoReplySubjects.Any(subject.Contains)) return InboundKind.AutoReply;

        return InboundKind.Reply;
    }

    /// <summary>Geri donen mailin govdesinde gecen ve bizim gonderdigimiz adreslerden biri.</summary>
    public static string? BouncedRecipient(InboundMessage m, IReadOnlyCollection<string> knownRecipients)
    {
        if (string.IsNullOrEmpty(m.BodyText)) return null;
        var known = new HashSet<string>(knownRecipients, StringComparer.OrdinalIgnoreCase);
        return EmailRegex.Matches(m.BodyText).Select(x => x.Value.Trim('.')).FirstOrDefault(known.Contains);
    }

    public static string NormalizeMessageId(string id) => id.Trim().Trim('<', '>').ToLowerInvariant();
}

/// <summary>
/// Gelen kutusunu IMAP ile okur. Cevap gelen lead'in RepliedAt'i dolar ve dizisi durur;
/// geri donen mailde kisinin e-postasi "Geçersiz" olur. Mailler okunmus isaretlenmez, tasinmaz.
/// </summary>
public class ReplyDetectionService
{
    private readonly ApplicationDbContext _db;
    private readonly ISettingsService _settings;
    private readonly SequenceService _sequences;
    private readonly ILogger<ReplyDetectionService> _logger;

    public ReplyDetectionService(ApplicationDbContext db, ISettingsService settings, SequenceService sequences,
        ILogger<ReplyDetectionService> logger)
    {
        _db = db;
        _settings = settings;
        _sequences = sequences;
        _logger = logger;
    }

    public record ImapConfig(string Host, int Port, string Username, string Password)
    {
        public bool IsComplete => Host.Length > 0 && Username.Length > 0 && Password.Length > 0;
    }

    /// <summary>IMAP bilgisi; kullanici adi/sifre bossa SMTP bilgileri kullanilir.</summary>
    public async Task<ImapConfig> ConfigAsync(CancellationToken ct = default)
    {
        async Task<string> Get(string key) => (await _settings.GetAsync(key, ct))?.Trim() ?? string.Empty;
        var user = await Get(SettingKeys.ImapUsername);
        var pass = await Get(SettingKeys.ImapPassword);
        if (user.Length == 0) user = await Get(SettingKeys.SmtpUsername) is { Length: > 0 } u ? u : await Get(SettingKeys.SmtpFromAddress);
        if (pass.Length == 0) pass = await Get(SettingKeys.SmtpPassword);
        var port = int.TryParse(await Get(SettingKeys.ImapPort), out var p) && p > 0 ? p : 993;
        return new ImapConfig(await Get(SettingKeys.ImapHost), port, user, pass);
    }

    public async Task<(bool Ok, string Message)> TestAsync(CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        if (!cfg.IsComplete) return (false, "IMAP sunucusu, kullanıcı adı veya şifre eksik.");
        try
        {
            using var client = await ConnectAsync(cfg, ct);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
            var count = inbox.Count;
            await client.DisconnectAsync(true, ct);
            return (true, $"Gelen kutusuna bağlanıldı ({count} mesaj).");
        }
        catch (Exception ex)
        {
            return (false, ex is AuthenticationException
                ? "Sunucu şifreyi kabul etmedi (Gmail/Outlook için uygulama şifresi gerekir)."
                : ex.Message);
        }
    }

    public async Task<int> CheckAsync(CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        if (!cfg.IsComplete) return 0;

        // Yalnizca mail gonderilmis lead varsa okumaya deger.
        if (!await _db.SentEmails.AnyAsync(ct)) return 0;

        var processed = 0;
        try
        {
            using var client = await ConnectAsync(cfg, ct);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

            var (validity, lastUid) = ParseCursor(await _settings.GetAsync(SettingKeys.ImapCursor, ct));
            IList<UniqueId> uids;
            if (validity != inbox.UidValidity || lastUid == 0)
                uids = await inbox.SearchAsync(SearchQuery.DeliveredAfter(DateTime.UtcNow.AddDays(-14)), ct);
            else
                uids = (await inbox.SearchAsync(SearchQuery.Uids(new UniqueIdRange(new UniqueId(lastUid + 1), UniqueId.MaxValue)), ct))
                    .Where(u => u.Id > lastUid).ToList();

            var maxUid = lastUid;
            foreach (var uid in uids.OrderBy(u => u.Id).Take(300))
            {
                var mime = await inbox.GetMessageAsync(uid, ct);
                if (await HandleAsync(ToInbound(mime), ct)) processed++;
                maxUid = Math.Max(maxUid, uid.Id);
            }

            await _db.SaveChangesAsync(ct);
            await _settings.SetManyAsync(new Dictionary<string, string?>
            {
                [SettingKeys.ImapCursor] = $"{inbox.UidValidity}:{maxUid}",
                [SettingKeys.ImapLastCheck] = DateTime.UtcNow.ToString("O"),
                [SettingKeys.ImapLastError] = null
            }, ct);

            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Gelen kutusu okunamadı.");
            await _settings.SetManyAsync(new Dictionary<string, string?>
            {
                [SettingKeys.ImapLastCheck] = DateTime.UtcNow.ToString("O"),
                [SettingKeys.ImapLastError] = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message
            }, ct);
        }

        if (processed > 0) _logger.LogInformation("Gelen kutusu: {Count} cevap/geri dönüş işlendi.", processed);
        return processed;
    }

    /// <summary>Tek gelen maili isler. Eslesen lead varsa true.</summary>
    public async Task<bool> HandleAsync(InboundMessage m, CancellationToken ct)
    {
        switch (ReplyMatcher.Classify(m))
        {
            case InboundKind.AutoReply:
                return false; // izin/ofis disi: dizi devam eder

            case InboundKind.Bounce:
            {
                var recipients = await _db.SentEmails.AsNoTracking()
                    .Where(e => e.SentAt >= m.Date.AddDays(-30))
                    .Select(e => e.ToAddress).Distinct().ToListAsync(ct);
                var bounced = ReplyMatcher.BouncedRecipient(m, recipients);
                if (bounced is null) return false;

                var contacts = await _db.Contacts.Where(c => c.Email != null && c.Email.ToLower() == bounced.ToLower()).ToListAsync(ct);
                foreach (var c in contacts)
                {
                    c.EmailStatus = EmailStatus.Invalid;
                    c.EmailStatusReason = "Mail geri döndü (adres kullanılmıyor olabilir).";
                    c.EmailCheckedAt = DateTime.UtcNow;
                }

                var contactIds = contacts.Select(c => c.Id).ToList();
                var leadIds = await _db.Leads.Where(l => l.ContactId != null && contactIds.Contains(l.ContactId.Value))
                    .Select(l => l.Id).ToListAsync(ct);
                foreach (var id in leadIds) await _sequences.StopForLeadAsync(id, "Mail geri döndü", ct);
                return contacts.Count > 0;
            }

            default:
            {
                var lead = await FindLeadAsync(m, ct);
                if (lead is null) return false;

                // Sadece bizim mailimizden SONRA gelen mesaj cevap sayilir.
                if (lead.SentAt is not null && m.Date < lead.SentAt.Value.AddMinutes(-1)) return false;

                lead.RepliedAt ??= m.Date;
                lead.SnoozedUntil = null;
                await _sequences.StopForLeadAsync(lead.Id, "Cevap geldi", ct);
                return true;
            }
        }
    }

    private async Task<Lead?> FindLeadAsync(InboundMessage m, CancellationToken ct)
    {
        if (m.ReferencedMessageIds.Count > 0)
        {
            var ids = m.ReferencedMessageIds.Select(ReplyMatcher.NormalizeMessageId).ToList();
            var sent = await _db.SentEmails.AsNoTracking()
                .Where(e => e.MessageId != null && ids.Contains(e.MessageId.ToLower().Replace("<", "").Replace(">", "")))
                .OrderByDescending(e => e.SentAt).FirstOrDefaultAsync(ct);
            if (sent is not null) return await _db.Leads.FirstOrDefaultAsync(l => l.Id == sent.LeadId, ct);
        }

        if (string.IsNullOrWhiteSpace(m.FromAddress)) return null;
        var from = m.FromAddress.Trim().ToLowerInvariant();

        // Adres eslesmesi: bu adrese mail gitmis en son lead.
        var leadId = await _db.SentEmails.AsNoTracking()
            .Where(e => e.ToAddress.ToLower() == from)
            .OrderByDescending(e => e.SentAt).Select(e => (int?)e.LeadId).FirstOrDefaultAsync(ct);
        return leadId is null ? null : await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
    }

    private static InboundMessage ToInbound(MimeMessage mime)
    {
        var refs = new List<string>();
        if (!string.IsNullOrWhiteSpace(mime.InReplyTo)) refs.Add(mime.InReplyTo);
        refs.AddRange(mime.References);

        var body = mime.TextBody ?? (mime.HtmlBody is null ? null : TextExtractor.ToPlainText(mime.HtmlBody));
        // Geri donen mailin orijinal alicisi cogu zaman ek (message/delivery-status) icindedir.
        foreach (var part in mime.BodyParts.OfType<MessageDeliveryStatus>())
            body += "\n" + string.Join("\n", part.StatusGroups.SelectMany(g => g).Select(h => h.Value));

        return new InboundMessage(
            mime.From.Mailboxes.FirstOrDefault()?.Address,
            mime.Subject,
            mime.Date == DateTimeOffset.MinValue ? DateTime.UtcNow : mime.Date.UtcDateTime,
            refs,
            mime.Headers["Auto-Submitted"],
            body);
    }

    private static async Task<ImapClient> ConnectAsync(ImapConfig cfg, CancellationToken ct)
    {
        var client = new ImapClient { Timeout = 20000 };
        await client.ConnectAsync(cfg.Host, cfg.Port, cfg.Port == 993 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, ct);
        await client.AuthenticateAsync(cfg.Username, cfg.Password, ct);
        return client;
    }

    private static (uint Validity, uint LastUid) ParseCursor(string? raw)
    {
        var parts = (raw ?? string.Empty).Split(':');
        return parts.Length == 2 && uint.TryParse(parts[0], out var v) && uint.TryParse(parts[1], out var u) ? (v, u) : (0, 0);
    }
}

/// <summary>5 dakikada bir gelen kutusunu kontrol eder.</summary>
public class ReplyDetectionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReplyDetectionWorker> _logger;

    public ReplyDetectionWorker(IServiceScopeFactory scopes, ILogger<ReplyDetectionWorker> logger)
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
                await scope.ServiceProvider.GetRequiredService<ReplyDetectionService>().CheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cevap algılama turu başarısız.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
