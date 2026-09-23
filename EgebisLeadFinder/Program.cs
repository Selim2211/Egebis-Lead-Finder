using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Extensions.Http;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Konsol ve gunluk dosyasina log. Uzun suren aramalarda ne olup bittigini
// sonradan izleyebilmek icin dosya logu onemli.
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/egebis-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

builder.Services.AddControllersWithViews();

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

builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

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

app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseSession();
app.UseAuthorization();
app.MapStaticAssets();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
