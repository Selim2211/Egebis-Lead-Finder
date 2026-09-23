using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>
/// Anahtar-deger seklinde calisma zamani ayari. API anahtarlari ve arama
/// unvanlari gibi, uygulamayi yeniden derlemeden degistirilmesi gereken
/// degerler burada tutulur.
/// </summary>
public class AppSetting
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Ayar anahtarlari. Yazim hatasini onlemek icin string yerine bunlar kullanilir;
/// ayni anahtarlar hem okuma hem Ayarlar ekraninda gecerli.
/// </summary>
public static class SettingKeys
{
    public const string SerperApiKey = "Search:SerperApiKey";
    public const string GeminiApiKey = "Ai:GeminiApiKey";

    /// <summary>Kullanilacak Gemini modeli (ör. gemini-3.6-flash). Bossa appsettings Ai:GeminiModel.</summary>
    public const string GeminiModel = "Ai:GeminiModel";
    public const string ApolloApiKey = "Apollo:ApiKey";

    /// <summary>Lead ararken kullanilacak unvan anahtar kelimeleri (virgulle ayrilmis).</summary>
    public const string LeadTitleKeywords = "Enrichment:TitleKeywords";

    /// <summary>
    /// Serper hesabindaki toplam kredi (ör. ücretsiz plan 2500). Yeni anahtar
    /// alindiginda kullanici bu degeri günceller; kullanim sayaci ayrı tutulur
    /// (bkz. IApiUsageTracker / ApiUsage tablosu).
    /// </summary>
    public const string SerperCreditLimit = "Search:SerperCreditLimit";

    /// <summary>
    /// Gemini cagri basina tahmini maliyet (TL). Uygulama token sayisi tutmadigi
    /// icin kesin maliyet hesaplanamaz; kullanici Google AI Studio faturasindan
    /// ortalama bir deger girer, sistem bunu cagri sayisiyla carpar.
    /// </summary>
    public const string GeminiCostPerCallTry = "Ai:GeminiCostPerCallTry";

    /// <summary>
    /// Canli USD/TRY kuru cekilemediginde kullanilacak son basarili deger
    /// (her basarili kur cekiminde otomatik guncellenir; kullanici duzenlemez).
    /// </summary>
    public const string LastKnownUsdTryRate = "Ai:LastKnownUsdTryRate";

    /// <summary>Firma aramasinda islenecek/getirilecek azami firma (skora gore en yukseklerden).</summary>
    public const string SearchMaxCompanies = "Search:MaxCompanies";

    /// <summary>Temel aramada (il secilmeden) taranacak ulke — eski ayar, bolge secimine devredildi.</summary>
    public const string SearchDefaultCountry = "Search:DefaultCountry";

    /// <summary>Arama bolgesi anahtari (bkz. SearchRegions): TR, DE, AE...</summary>
    public const string SearchRegion = "Search:Region";

    /// <summary>Arama sonuclarindan elenecek ek alan adlari (virgulle ayrilmis).</summary>
    public const string ExtraBlockedDomains = "Search:ExtraBlockedDomains";

    /// <summary>Mail atildiktan kac gun sonra lead "takip bekliyor" sayilir.</summary>
    public const string FollowUpAfterDays = "Leads:FollowUpAfterDays";

    public const int DefaultFollowUpAfterDays = 5;

    // --- E-posta gonderimi (SMTP). Anahtarlar appsettings "Smtp" bolumuyle ayni:
    // ekranda bos birakilirsa yapilandirmadaki degere dusulur. ---
    public const string SmtpFromAddress = "Smtp:FromAddress";
    public const string SmtpFromName = "Smtp:FromName";
    public const string SmtpHost = "Smtp:Host";
    public const string SmtpPort = "Smtp:Port";
    public const string SmtpUsername = "Smtp:Username";
    public const string SmtpPassword = "Smtp:Password";

    /// <summary>"starttls" | "ssl" | "auto".</summary>
    public const string SmtpSecurity = "Smtp:Security";

    public const int DefaultSearchMaxCompanies = 100;
    public const int SearchMaxCompaniesUpperLimit = 200;
    public const string DefaultSearchCountry = "Türkiye";

    /// <summary>Ayarlar ekranindaki tum anahtarlar.</summary>
    public static readonly string[] All =
    {
        LeadTitleKeywords, SerperApiKey, GeminiApiKey, GeminiModel, ApolloApiKey, SerperCreditLimit,
        GeminiCostPerCallTry, LastKnownUsdTryRate, SearchMaxCompanies, SearchDefaultCountry, SearchRegion, FollowUpAfterDays, ExtraBlockedDomains,
        SmtpFromAddress, SmtpFromName, SmtpHost, SmtpPort, SmtpUsername, SmtpPassword, SmtpSecurity
    };

    /// <summary>Deger gizlenmeli mi? API anahtarlari ve e-posta sifresi ekranda maskeli gosterilir.</summary>
    public static bool IsSecret(string key) =>
        key is SerperApiKey or GeminiApiKey or ApolloApiKey or SmtpPassword;

    /// <summary>
    /// Yalnizca Ayarlar ekranindan okunan anahtarlar: bos birakilirsa User Secrets /
    /// appsettings'e dusulmez, ozellik "anahtar eksik" hatasi verir.
    /// </summary>
    public static bool IsSettingsOnly(string key) => key is GeminiApiKey;
}
