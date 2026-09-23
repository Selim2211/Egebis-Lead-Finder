using System.Net.Mail;
using DnsClient;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EgebisLeadFinder.Services;

public record EmailCheckResult(EmailStatus Status, string Reason);

/// <summary>Alan adinin posta kabul edip etmedigi. Unknown = DNS'e ulasilamadi.</summary>
public enum MxLookup { HasMail, NoMail, NoDomain, Unknown }

public interface IMxResolver
{
    Task<MxLookup> LookupAsync(string domain, CancellationToken ct = default);
}

/// <summary>MX kaydi; yoksa A kaydi (RFC 5321 "implicit MX") aranir.</summary>
public class DnsMxResolver : IMxResolver
{
    private static readonly LookupClient Client = new(new LookupClientOptions
    {
        Timeout = TimeSpan.FromSeconds(5),
        Retries = 1,
        UseCache = true
    });

    public async Task<MxLookup> LookupAsync(string domain, CancellationToken ct = default)
    {
        try
        {
            var mx = await Client.QueryAsync(domain, QueryType.MX, cancellationToken: ct);
            if (mx.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain) return MxLookup.NoDomain;
            if (mx.HasError) return MxLookup.Unknown;
            if (mx.Answers.MxRecords().Any(r => r.Exchange.Value.Trim('.').Length > 0)) return MxLookup.HasMail;

            var a = await Client.QueryAsync(domain, QueryType.A, cancellationToken: ct);
            if (a.HasError) return MxLookup.Unknown;
            return a.Answers.ARecords().Any() ? MxLookup.HasMail : MxLookup.NoMail;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return MxLookup.Unknown;
        }
    }
}

/// <summary>
/// E-posta dogrulama: bicim, tek kullanimlik alan adi, alan adinin posta sunucusu (MX)
/// ve genel adres (info@, satis@...) kontrolu. Posta kutusu SMTP ile yoklanmaz:
/// 25 portu cogu agda kapali ve yoklama gonderici itibarini bozar.
/// </summary>
public class EmailVerificationService
{
    private static readonly HashSet<string> RolePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "bilgi", "iletisim", "contact", "hello", "merhaba", "satis", "sales", "pazarlama", "marketing",
        "muhasebe", "finans", "finance", "accounting", "destek", "support", "yardim", "help", "admin", "office",
        "ofis", "ik", "hr", "insankaynaklari", "kariyer", "career", "careers", "jobs", "noreply", "no-reply",
        "export", "ihracat", "import", "satinalma", "purchasing", "procurement", "mail", "webmaster", "genel",
        "musteri", "customer", "service", "servis", "teknik", "kalite", "quality", "lojistik", "logistics", "sekreter"
    };

    private static readonly Lazy<HashSet<string>> DisposableDomains = new(LoadDisposable);

    private readonly IMxResolver _mx;
    private readonly IMemoryCache _cache;

    public EmailVerificationService(IMxResolver mx, IMemoryCache cache)
    {
        _mx = mx;
        _cache = cache;
    }

    public async Task<EmailCheckResult> VerifyAsync(string? email, CancellationToken ct = default)
    {
        var address = email?.Trim() ?? string.Empty;

        if (address.Length == 0 || address.Contains('*') || address.Contains(' ')
            || !MailAddress.TryCreate(address, out var parsed) || parsed.Address != address
            || !parsed.Host.Contains('.'))
            return new EmailCheckResult(EmailStatus.Invalid, "Adres biçimi hatalı.");

        var local = parsed.User;
        var domain = parsed.Host.ToLowerInvariant();

        if (DisposableDomains.Value.Contains(domain))
            return new EmailCheckResult(EmailStatus.Risky, "Tek kullanımlık e-posta servisi.");

        var mx = await _cache.GetOrCreateAsync($"mx:{domain}", async entry =>
        {
            var result = await _mx.LookupAsync(domain, ct);
            // DNS'e ulasilamadiysa kisa sure tut: gecici ag hatasi kalici sonuc olmasin.
            entry.AbsoluteExpirationRelativeToNow = result == MxLookup.Unknown ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(24);
            return result;
        });

        return mx switch
        {
            MxLookup.NoDomain => new EmailCheckResult(EmailStatus.Invalid, $"{domain} alan adı yok."),
            MxLookup.NoMail => new EmailCheckResult(EmailStatus.Invalid, $"{domain} e-posta kabul etmiyor (MX kaydı yok)."),
            MxLookup.Unknown => new EmailCheckResult(EmailStatus.Risky, "Alan adı şu an doğrulanamadı."),
            _ => IsRole(local)
                ? new EmailCheckResult(EmailStatus.Role, "Genel adres; karar vericiye doğrudan ulaşmayabilir.")
                : new EmailCheckResult(EmailStatus.Valid, "Alan adı e-posta kabul ediyor.")
        };
    }

    /// <summary>Sonucu kisiye yazar (kaydetmez).</summary>
    public async Task ApplyAsync(Contact contact, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contact.Email))
        {
            contact.EmailStatus = EmailStatus.Unchecked;
            contact.EmailStatusReason = null;
            contact.EmailCheckedAt = null;
            return;
        }

        var result = await VerifyAsync(contact.Email, ct);
        contact.EmailStatus = result.Status;
        contact.EmailStatusReason = result.Reason;
        contact.EmailCheckedAt = DateTime.UtcNow;
    }

    public static bool IsRole(string localPart)
    {
        var key = localPart.ToLowerInvariant().Split('+')[0];
        return RolePrefixes.Contains(key) || RolePrefixes.Contains(key.Replace(".", "").Replace("-", "").Replace("_", ""));
    }

    private static HashSet<string> LoadDisposable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "reference", "disposable-domains.txt");
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return set;
        foreach (var line in File.ReadAllLines(path))
        {
            var d = line.Trim();
            if (d.Length > 0 && !d.StartsWith('#')) set.Add(d);
        }
        return set;
    }
}

/// <summary>Dogrulanmamis e-postalari arka planda (dakikada bir, 50'ser) dogrular.</summary>
public class EmailVerificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailVerificationWorker> _logger;

    public EmailVerificationWorker(IServiceScopeFactory scopes, ILogger<EmailVerificationWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var verifier = scope.ServiceProvider.GetRequiredService<EmailVerificationService>();

                var contacts = await db.Contacts
                    .Where(c => c.Email != null && c.Email != "" && c.EmailCheckedAt == null)
                    .OrderBy(c => c.Id)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                foreach (var contact in contacts)
                    await verifier.ApplyAsync(contact, stoppingToken);

                if (contacts.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("{Count} e-posta doğrulandı.", contacts.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "E-posta doğrulama turu başarısız.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
