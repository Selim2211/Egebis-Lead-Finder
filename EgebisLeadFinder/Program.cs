using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Localization;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Extensions.Http;
using Serilog;

// Arka plan işleri (mail dizisi vb.) istek dışında çalışır: arayüz dili varsayılan olarak Türkçe kalır.
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("tr-TR");

var builder = WebApplication.CreateBuilder(args);

// Konsol ve gunluk dosyasina log. Uzun suren aramalarda ne olup bittigini
// sonradan izleyebilmek icin dosya logu onemli.
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/egebis-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

// Her sayfa giris ister (AllowAnonymous olanlar haric); tum degistirici islemler audit log'a yazilir.
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AuthorizeFilter());
    o.Filters.Add<AuditActionFilter>();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<UserService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<UsageService>();
builder.Services.AddScoped<SearchRunService>();
builder.Services.AddScoped<FavoriteService>();
builder.Services.AddSingleton<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<AuditActionFilter>();
builder.Services.AddHostedService<AuditCleanupWorker>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Account/Denied";
        o.Cookie.Name = "egebis_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;

        // Kullanici pasiflestirildi, silindi, rolu/sifresi degisti: eski cerez hemen gecersiz olur.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var id = ctx.Principal?.UserId();
            var stamp = ctx.Principal?.FindFirst(UserService.StampClaim)?.Value;
            var users = ctx.HttpContext.RequestServices.GetRequiredService<UserService>();
            var user = id is null ? null : await users.FindAsync(id.Value);

            if (user is null || !user.IsActive || user.SecurityStamp != stamp)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization();

// Konteynerde oturum/antiforgery anahtarlari kalici klasorde tutulur; yoksa her yeniden
// baslatmada formlar ve Salesforce OAuth oturumu gecersiz kalir.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    builder.Services.AddDataProtection()
        .SetApplicationName("EgebisLeadFinder")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.AddMemoryCache();

// Salesforce OAuth akisindaki state (CSRF) degerini tutmak icin oturum.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(10);
    o.Cookie.HttpOnly = true;
});

// Uzun suren islerin (firma aramasi, on arastirma) ilerlemesini tutan bellek-ici depo.
builder.Services.AddSingleton<EgebisLeadFinder.Services.Progress.JobProgressStore>();

// Database:Name verilirse ayni sunucuda baska bir veritabani kullanilir (ör. deneme/test veritabani).
var connectionString = builder.Configuration.GetConnectionString("Default");
if (builder.Configuration["Database:Name"] is { Length: > 0 } databaseName)
    connectionString = new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;

builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connectionString));

// Kritik alan sifrelemesi (kisi e-posta/telefonu, kullanici bilgileri, API anahtarlari). Anahtar
// "Encryption:Key" ayarindan (base64, 32 bayt) ya da anahtar dosyasindan gelir; dosya yoksa ilk
// acilista uretilir. Anahtar kaybolursa sifreli veriler okunamaz: dosyayi yedekleyin.
var encryptionKeyFile = builder.Configuration["Encryption:KeyFile"] is { Length: > 0 } configuredKeyFile
    ? configuredKeyFile
    : Path.Combine(keysPath is { Length: > 0 } ? keysPath : Path.Combine(builder.Environment.ContentRootPath, "App_Data"),
        "field-encryption.key");
var (fieldEncryption, encryptionKeySource, encryptionKeyCreated) =
    EgebisLeadFinder.Services.Security.FieldEncryption.Load(builder.Configuration["Encryption:Key"], encryptionKeyFile);
EgebisLeadFinder.Services.Security.FieldEncryption.Current = fieldEncryption;

// Ayarlar servisi singleton: firma analizleri paralel calisiyor ve DbContext
// thread-safe degil, bu yuzden servis her okumada kendi scope'unu aciyor.
builder.Services.AddSingleton<ISettingsService, SettingsService>();

// API kullanim sayaci (Serper kredi barı icin) ayni sebeple singleton.
builder.Services.AddSingleton<IApiUsageTracker, ApiUsageTracker>();

// Gemini maliyet tahmini icin guncel USD/TRY kuru (Ayarlar ekrani).
builder.Services.AddHttpClient<IExchangeRateService, ExchangeRateService>(c => c.Timeout = TimeSpan.FromSeconds(6));

// appsettings bolumleri -> tipli secenekler
builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection(SearchOptions.Section));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.Section));
builder.Services.Configure<ScraperOptions>(builder.Configuration.GetSection(ScraperOptions.Section));
builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.Section));
builder.Services.Configure<ScoringOptions>(builder.Configuration.GetSection(ScoringOptions.Section));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.Section));
builder.Services.Configure<ApolloOptions>(builder.Configuration.GetSection(ApolloOptions.Section));
builder.Services.Configure<EnrichmentOptions>(builder.Configuration.GetSection(EnrichmentOptions.Section));
builder.Services.Configure<ResearchOptions>(builder.Configuration.GetSection(ResearchOptions.Section));
builder.Services.Configure<SalesforceOptions>(builder.Configuration.GetSection(SalesforceOptions.Section));

// 429 (kota) ve gecici sunucu hatalarinda artan beklemeyle (2sn, 4sn, 8sn) tekrar dener.
static IAsyncPolicy<HttpResponseMessage> ExponentialBackoffPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(response => (int)response.StatusCode == 429)
        .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

// Search saglayicisi appsettings -> Search:Provider ile secilir.
var searchProvider = builder.Configuration["Search:Provider"] ?? "Mock";
if (string.Equals(searchProvider, "Serper", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddHttpClient<ISearchService, SerperSearchService>(c =>
        c.Timeout = TimeSpan.FromSeconds(30))
        .AddPolicyHandler(ExponentialBackoffPolicy());
else
    builder.Services.AddSingleton<ISearchService, MockSearchService>();

builder.Services.AddHttpClient<IWebScraperService, WebScraperService>(c =>
{
    var scraper = builder.Configuration.GetSection(ScraperOptions.Section).Get<ScraperOptions>() ?? new ScraperOptions();
    c.Timeout = TimeSpan.FromSeconds(scraper.TimeoutSeconds);
});

builder.Services.AddScoped<LeadScoringService>();
builder.Services.AddScoped<LeadDiscoveryService>();
builder.Services.AddScoped<EmailTemplateService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddHttpClient<IGeminiModelCatalog, GeminiModelCatalog>(c => c.Timeout = TimeSpan.FromSeconds(10));

// Zaman asimi istek basina GeminiAiService icinde uygulanir: derin firma analizi
// (uzun girdi/cikti) normal site analizinden daha uzun surer.
builder.Services.AddHttpClient<IAiService, GeminiAiService>(c => c.Timeout = Timeout.InfiniteTimeSpan);

// 401/403 (yetki hatasi) burada yakalanmaz; ApolloPersonEmailFinder onlari
// tekrar denemeden acikca raporlar. Sadece 429 ve gecici sunucu hatalari icin
// ExponentialBackoffPolicy devreye girer.
builder.Services.AddHttpClient<IPersonEmailFinder, ApolloPersonEmailFinder>(c =>
{
    var apollo = builder.Configuration.GetSection(ApolloOptions.Section).Get<ApolloOptions>() ?? new ApolloOptions();
    c.Timeout = TimeSpan.FromSeconds(apollo.TimeoutSeconds);
}).AddPolicyHandler(ExponentialBackoffPolicy());

// Kisi aramasi: karar vericileri domain + unvan ile dogrudan bulur. Erisim yoksa
// HybridContactEnrichmentService LinkedIn yedegine duser.
builder.Services.AddHttpClient<IPeopleSearchService, ApolloPeopleSearchService>(c =>
{
    var apollo = builder.Configuration.GetSection(ApolloOptions.Section).Get<ApolloOptions>() ?? new ApolloOptions();
    c.Timeout = TimeSpan.FromSeconds(apollo.TimeoutSeconds);
}).AddPolicyHandler(ExponentialBackoffPolicy());

builder.Services.AddScoped<IContactEnrichmentService, HybridContactEnrichmentService>();
builder.Services.AddScoped<ContactRevealService>();

// ================= Faz-II: firma on arastirma / rating =================

// AI #3 ayni GeminiAiService ornegidir; ICompanyRatingAi ona yonlendirilir.
builder.Services.AddScoped<ICompanyRatingAi>(sp => (ICompanyRatingAi)sp.GetRequiredService<IAiService>());
builder.Services.AddScoped<IEmailWriterAi>(sp => (IEmailWriterAi)sp.GetRequiredService<IAiService>());
builder.Services.AddScoped<EmailDraftService>();
builder.Services.AddScoped<INaceClassifierAi>(sp => (INaceClassifierAi)sp.GetRequiredService<IAiService>());
builder.Services.AddScoped<IcpService>();
builder.Services.AddScoped<IBusinessProfileAi>(sp => (IBusinessProfileAi)sp.GetRequiredService<IAiService>());
builder.Services.AddScoped<BusinessProfileService>();
builder.Services.AddScoped<ISearchPlannerAi>(sp => (ISearchPlannerAi)sp.GetRequiredService<IAiService>());
builder.Services.AddScoped<SearchPlanService>();
builder.Services.AddScoped<IInsightAi>(sp => (IInsightAi)sp.GetRequiredService<IAiService>());
builder.Services.AddScoped<ITemplateWriterAi>(sp => (ITemplateWriterAi)sp.GetRequiredService<IAiService>());
builder.Services.AddScoped<ProfileTemplateService>();
builder.Services.AddScoped<CompanyComparisonService>();
builder.Services.AddScoped<SectorAnalysisService>();
builder.Services.AddScoped<EgebisLeadFinder.Services.Sequences.SequenceService>();
builder.Services.AddScoped<EgebisLeadFinder.Services.Sequences.ReplyDetectionService>();
builder.Services.AddHostedService<EgebisLeadFinder.Services.Sequences.SequenceSenderWorker>();
builder.Services.AddHostedService<EgebisLeadFinder.Services.Sequences.ReplyDetectionWorker>();

// Kaynak toplayicilar. Hepsi ICompanyIntelSource olarak kaydedilir; orkestrator
// IEnumerable<ICompanyIntelSource> alir ve hepsini calistirir.
builder.Services.AddScoped<EgebisLeadFinder.Services.CompanyIntel.ICompanyIntelSource, EgebisLeadFinder.Services.CompanyIntel.NewsIntelSource>();
builder.Services.AddScoped<EgebisLeadFinder.Services.CompanyIntel.ICompanyIntelSource, EgebisLeadFinder.Services.CompanyIntel.RegistryLinkSource>();
builder.Services.AddScoped<EgebisLeadFinder.Services.CompanyIntel.ICompanyIntelSource, EgebisLeadFinder.Services.CompanyIntel.CompanyListSource>();
builder.Services.AddScoped<EgebisLeadFinder.Services.CompanyIntel.ICompanyIntelSource, EgebisLeadFinder.Services.CompanyIntel.WebsiteIntelSource>();

// KAP: uye listesi + son finansal rapor (KapFinancialClient) + site: bildirim aramasi.
builder.Services.AddHttpClient<EgebisLeadFinder.Services.CompanyIntel.KapFinancialClient>(c => c.Timeout = TimeSpan.FromSeconds(20))
    .AddPolicyHandler(ExponentialBackoffPolicy());
builder.Services.AddHttpClient<EgebisLeadFinder.Services.CompanyIntel.KapIntelSource>(c => c.Timeout = TimeSpan.FromSeconds(15))
    .AddPolicyHandler(ExponentialBackoffPolicy());
builder.Services.AddScoped<EgebisLeadFinder.Services.CompanyIntel.ICompanyIntelSource>(sp =>
    sp.GetRequiredService<EgebisLeadFinder.Services.CompanyIntel.KapIntelSource>());

builder.Services.AddScoped<CompanyRatingEvaluator>();
builder.Services.AddScoped<ICompanyResearchService, CompanyResearchService>();

// ================= CRM: Salesforce =================
builder.Services.AddSingleton<IMxResolver, DnsMxResolver>();
builder.Services.AddScoped<EmailVerificationService>();
builder.Services.AddHostedService<EmailVerificationWorker>();
builder.Services.AddScoped<SalesforceSyncRunner>();
builder.Services.AddHostedService<SalesforceAutoSyncService>();
builder.Services.AddHttpClient<ISalesforceConnector, SalesforceConnector>(c =>
{
    var sf = builder.Configuration.GetSection(SalesforceOptions.Section).Get<SalesforceOptions>() ?? new SalesforceOptions();
    c.Timeout = TimeSpan.FromSeconds(sf.TimeoutSeconds);
}).AddPolicyHandler(ExponentialBackoffPolicy());

var app = builder.Build();

// ngrok gibi bir tunel/proxy arkasinda calisirken Request.Scheme ve Host'un
// gercek (dis) degerleri yansitmasi icin en basta olmali. ngrok'un yerel ajani
// Kestrel'e loopback uzerinden baglandigi icin varsayilan (loopback) guven
// listesi zaten yeterli; yine de acik olmasi icin listeler temizlenir.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Sunucu kurulumunda tablolar elle kurulmaz: acilista bekleyen migration'lar uygulanir.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
}

if (encryptionKeyCreated)
    app.Logger.LogWarning("Yeni şifreleme anahtarı oluşturuldu: {KeyFile}. Bu dosyayı yedekleyin; kaybolursa şifreli veriler okunamaz.",
        encryptionKeySource);
else
    app.Logger.LogInformation("Şifreleme anahtarı yüklendi ({Source}, anahtar {KeyId}).", encryptionKeySource, fieldEncryption.KeyId);

// Sifreleme oncesinden kalan duz metin kritik alanlar sifrelenir (tekrar calismasi guvenli).
try
{
    using var scope = app.Services.CreateScope();
    await EgebisLeadFinder.Services.Security.EncryptionBackfill.RunAsync(
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), app.Logger);
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Kritik alanlar şifrelenemedi; veritabanı güncel değilse migration'ları uygulayın.");
}

app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
// Arayüz dili (cookie); İngilizce'de HTML/JSON/JS çıktısını sözlükten çevirir.
Loc.TrackMissing = app.Environment.IsDevelopment();
app.UseMiddleware<LocalizationMiddleware>();
app.UseRouting();
app.UseSession();
app.UseAuthentication();

// Hic kullanici yoksa (ilk kurulum) her istek yonetici olusturma ekranina gider.
// Yonetici sifresini sifirladiysa kullanici once yeni sifre belirler.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isAsset = path.StartsWithSegments("/css") || path.StartsWithSegments("/js") || path.StartsWithSegments("/lib")
        || path.StartsWithSegments("/img") || path.StartsWithSegments("/health") || path.Value?.EndsWith(".ico") == true
        || path.Value?.EndsWith(".css") == true;

    if (!isAsset && !path.StartsWithSegments("/Account/Setup") && !SetupState.HasUsers)
    {
        var users = context.RequestServices.GetRequiredService<UserService>();
        if (await users.AnyUsersAsync(context.RequestAborted)) SetupState.HasUsers = true;
        else
        {
            context.Response.Redirect("/Account/Setup");
            return;
        }
    }

    if (!isAsset && context.User.HasClaim(UserService.MustChangeClaim, "1")
        && !path.StartsWithSegments("/Account/ChangePassword") && !path.StartsWithSegments("/Account/Logout"))
    {
        context.Response.Redirect("/Account/ChangePassword");
        return;
    }

    await next();
});

app.UseAuthorization();
app.MapStaticAssets();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
if (app.Environment.IsDevelopment())
    app.MapGet("/_i18n/missing", () => Results.Text(string.Join("\n", Loc.MissingTexts), "text/plain; charset=utf-8")).AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
