using EgebisLeadFinder.Data;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services.Diagnostics;

/// <summary>
/// Uygulama acilir acilmaz ilk kullanicinin odeyecegi "soguk baslangic" bedelini (EF model ve
/// sorgu derlemesi, MVC/Razor hatti, statik dosya ve sikistirma) arka planda oder. Yogun sunucuda
/// ilk sayfa 10-20 sn surebiliyordu; isinma bitince ilk istek de normal hizda acilir.
/// </summary>
public class StartupWarmup : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IServer _server;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<StartupWarmup> _logger;

    public StartupWarmup(IServiceProvider services, IServer server, IHostApplicationLifetime lifetime,
        IHttpClientFactory httpFactory, ILogger<StartupWarmup> logger)
    {
        _services = services;
        _server = server;
        _lifetime = lifetime;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource();
        using (_lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
            await started.Task.WaitAsync(stoppingToken);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Companies.AsNoTracking().Include(c => c.Contacts).Take(1).ToListAsync(stoppingToken);
                await db.Leads.AsNoTracking().Take(1).ToListAsync(stoppingToken);
                await db.AppSettings.AsNoTracking().Take(1).ToListAsync(stoppingToken);
                await db.Users.AsNoTracking().Take(1).ToListAsync(stoppingToken);
            }

            var address = _server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
            if (address is not null)
            {
                var baseUrl = address.Replace("://+", "://localhost").Replace("://*", "://localhost").Replace("://[::]", "://localhost").Replace("://0.0.0.0", "://localhost");
                using var http = _httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(60);
                foreach (var path in new[] { "/health", "/Account/Login", "/css/site.css" })
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + path);
                    request.Headers.AcceptEncoding.ParseAdd("br, gzip");
                    using var _ = await http.SendAsync(request, stoppingToken);
                }
            }
            _logger.LogInformation("Açılış ısınması tamamlandı: {Ms} ms", (int)sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Açılış ısınması tamamlanamadı (uygulama normal çalışır).");
        }
    }
}
