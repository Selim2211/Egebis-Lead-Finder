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

    /// <summary>Ideal musteri profili (IcpProfile) JSON olarak.</summary>
    public const string IcpProfile = "Icp:Profile";

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

    // --- Otomatik mail dizileri ve cevap algilama (IMAP) ---
    public const string SequenceDailyCap = "Sequence:DailyCap";
    public const int DefaultSequenceDailyCap = 50;
    public const string ImapHost = "Imap:Host";
    public const string ImapPort = "Imap:Port";
    public const string ImapUsername = "Imap:Username";
    public const string ImapPassword = "Imap:Password";

    /// <summary>Son okunan mesajin UID'si ve klasorun UIDVALIDITY degeri ("validity:uid").</summary>
    public const string ImapCursor = "Imap:Cursor";
    public const string ImapLastCheck = "Imap:LastCheck";
    public const string ImapLastError = "Imap:LastError";

    // --- Salesforce CRM entegrasyonu. Username-Password OAuth flow: sunucu
    // taraflı, kullanici etkilesimi gerektirmez. Anahtarlar appsettings
    // "Salesforce" bolumuyle ayni. ---
    public const string SalesforceConsumerKey = "Salesforce:ConsumerKey";
    public const string SalesforceConsumerSecret = "Salesforce:ConsumerSecret";
    public const string SalesforceUsername = "Salesforce:Username";
    public const string SalesforcePassword = "Salesforce:Password";

    /// <summary>Kullanici sifresinin sonuna eklenir (Setup &gt; Reset My Security Token).</summary>
    public const string SalesforceSecurityToken = "Salesforce:SecurityToken";

    /// <summary>ör. https://login.salesforce.com veya https://test.salesforce.com (sandbox).</summary>
    public const string SalesforceLoginUrl = "Salesforce:LoginUrl";

    public const string DefaultSalesforceLoginUrl = "https://login.salesforce.com";

    // --- Salesforce OAuth (Authorization Code flow): "Salesforce'a Bağlan" akışında
    // elde edilir. Kullanici sifresini bilmemize gerek kalmaz; refresh token kalici
    // saklanir, access token bundan tazelenir. Username/Password alanlari (yukarida)
    // eski/yedek yontem olarak kalir. ---
    public const string SalesforceRefreshToken = "Salesforce:RefreshToken";
    public const string SalesforceInstanceUrl = "Salesforce:InstanceUrl";

    /// <summary>
    /// Uygulamanin disaridan erisilen adresi (ör. https://app.egebis.com veya ngrok adresi).
    /// Doluysa OAuth callback her zaman bu adresle kurulur; "Bağlan" localhost'tan
    /// baslatilsa bile once buraya yonlendirilir. Bos ise istegin geldigi adres kullanilir.
    /// </summary>
    public const string SalesforcePublicBaseUrl = "Salesforce:PublicBaseUrl";

    public const int DefaultSearchMaxCompanies = 100;
    public const int SearchMaxCompaniesUpperLimit = 200;
    public const string DefaultSearchCountry = "Türkiye";

    /// <summary>Ayarlar ekranindaki tum anahtarlar.</summary>
    public static readonly string[] All =
    {
        LeadTitleKeywords, SerperApiKey, GeminiApiKey, GeminiModel, ApolloApiKey, SerperCreditLimit,
        GeminiCostPerCallTry, LastKnownUsdTryRate, SearchMaxCompanies, SearchDefaultCountry, SearchRegion, FollowUpAfterDays, IcpProfile, ExtraBlockedDomains,
        SmtpFromAddress, SmtpFromName, SmtpHost, SmtpPort, SmtpUsername, SmtpPassword, SmtpSecurity,
        SalesforceConsumerKey, SalesforceConsumerSecret, SalesforceUsername, SalesforcePassword,
        SalesforceSecurityToken, SalesforceLoginUrl, SalesforceRefreshToken, SalesforceInstanceUrl,
        SalesforcePublicBaseUrl, SequenceDailyCap, ImapHost, ImapPort, ImapUsername, ImapPassword,
        ImapCursor, ImapLastCheck, ImapLastError
    };

    /// <summary>Deger gizlenmeli mi? API anahtarlari ve e-posta sifresi ekranda maskeli gosterilir.</summary>
    public static bool IsSecret(string key) =>
        key is SerperApiKey or GeminiApiKey or ApolloApiKey or SmtpPassword or ImapPassword
            or SalesforceConsumerSecret or SalesforcePassword or SalesforceSecurityToken or SalesforceRefreshToken;

    /// <summary>
    /// Yalnizca Ayarlar ekranindan okunan anahtarlar: bos birakilirsa User Secrets /
    /// appsettings'e dusulmez, ozellik "anahtar eksik" hatasi verir.
    /// </summary>
    public static bool IsSettingsOnly(string key) => key is GeminiApiKey;
}
