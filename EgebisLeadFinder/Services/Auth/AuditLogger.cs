using System.Security.Claims;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services.Auth;

/// <summary>Kim, ne zaman, ne yapti. Gizli degerler (sifre, anahtar) asla yazilmaz.</summary>
public interface IAuditLogger
{
    Task LogAsync(string action, string? summary = null, string? entityType = null, string? entityId = null,
        bool success = true, AuditActor? actor = null, CancellationToken ct = default);
}

/// <summary>Istek disindan (arka plan isleri, giris oncesi) kaydederken kimin adina yazilacagi.</summary>
public record AuditActor(int? UserId, string UserName, string? Ip = null, string? UserAgent = null)
{
    public static readonly AuditActor System = new(null, "Sistem");

    public static AuditActor From(HttpContext http, AppUser? user = null) => new(
        user?.Id ?? http.User.UserId(),
        user?.UserName ?? http.User.Identity?.Name ?? "anonim",
        AuditLogger.ClientIp(http),
        AuditLogger.Trim(http.Request.Headers.UserAgent.ToString(), 300));
}

public class AuditLogger : IAuditLogger
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(IServiceScopeFactory scopes, IHttpContextAccessor http, ILogger<AuditLogger> logger)
    {
        _scopes = scopes;
        _http = http;
        _logger = logger;
    }

    public async Task LogAsync(string action, string? summary = null, string? entityType = null, string? entityId = null,
        bool success = true, AuditActor? actor = null, CancellationToken ct = default)
    {
        var context = _http.HttpContext;
        actor ??= context is not null && context.User.Identity?.IsAuthenticated == true
            ? AuditActor.From(context)
            : context is not null ? AuditActor.From(context) with { UserName = "anonim" } : AuditActor.System;

        var entry = new AuditLog
        {
            At = DateTime.UtcNow,
            UserId = actor.UserId,
            UserName = Trim(actor.UserName, 64) ?? "anonim",
            Action = Trim(action, 64)!,
            EntityType = Trim(entityType, 40),
            EntityId = Trim(entityId, 40),
            Summary = Trim(summary, 1000),
            Ip = Trim(actor.Ip, 64),
            UserAgent = Trim(actor.UserAgent, 300),
            Success = success
        };

        try
        {
            // Ayri scope: istegin DbContext'indeki kaydedilmemis degisikliklere karismaz.
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AuditLogs.Add(entry);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Denetim kaydi yazilamadi diye kullanicinin islemi bozulmamali.
            _logger.LogError(ex, "Audit kaydı yazılamadı: {Action}", action);
        }
    }

    public static string? ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    public static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length > max ? value[..max] : value;
    }
}

/// <summary>Eski denetim kayitlarini gunde bir siler (Ayarlar'daki saklama suresi, varsayilan 365 gun).</summary>
public class AuditCleanupWorker : BackgroundService
{
    public const int DefaultRetentionDays = 365;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AuditCleanupWorker> _logger;

    public AuditCleanupWorker(IServiceScopeFactory scopes, ILogger<AuditCleanupWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                var days = int.TryParse(await settings.GetAsync(SettingKeys.AuditRetentionDays, stoppingToken), out var d) && d >= 30
                    ? d : DefaultRetentionDays;

                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var cutoff = DateTime.UtcNow.AddDays(-days);
                var removed = await db.AuditLogs.Where(a => a.At < cutoff).ExecuteDeleteAsync(stoppingToken);
                if (removed > 0) _logger.LogInformation("{Count} eski audit kaydı silindi ({Days} gün).", removed, days);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit temizliği başarısız.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
