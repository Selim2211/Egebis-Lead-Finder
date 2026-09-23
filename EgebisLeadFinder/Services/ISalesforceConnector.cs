namespace EgebisLeadFinder.Services;

/// <summary>Tek bir Salesforce senkron denemesinin sonucu.</summary>
public class SalesforceSyncResult
{
    public bool Success { get; init; }

    /// <summary>Basarili ise Salesforce'taki kaydin Id'si (Account veya Lead).</summary>
    public string? SalesforceId { get; init; }

    public string? Error { get; init; }

    public static SalesforceSyncResult Ok(string salesforceId) => new() { Success = true, SalesforceId = salesforceId };
    public static SalesforceSyncResult Failed(string error) => new() { Success = false, Error = error };
}

/// <summary>Salesforce ozel alan kurulumunun sonucu.</summary>
public class SalesforceSchemaResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>Bu calismada yeni olusturulan alanlar (ör. "Account.Egebis_Score__c").</summary>
    public IReadOnlyList<string> Created { get; init; } = Array.Empty<string>();

    /// <summary>Egebis alanlarinin eklendigi sayfa duzenleri (ör. "Account: Account Layout").</summary>
    public IReadOnlyList<string> LayoutsUpdated { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Kritik olmayan sorun (ör. bir sayfa duzeni guncellenemedi). Veri aktarimi yine calisir;
    /// yalnizca alanlar kayit sayfasinda gorunmeyebilir.
    /// </summary>
    public string? Warning { get; init; }

    public static SalesforceSchemaResult Ok(IReadOnlyList<string> created, IReadOnlyList<string>? layouts = null, string? warning = null) =>
        new() { Success = true, Created = created, LayoutsUpdated = layouts ?? Array.Empty<string>(), Warning = warning };
    public static SalesforceSchemaResult Failed(string error, IReadOnlyList<string>? created = null) =>
        new() { Success = false, Error = error, Created = created ?? Array.Empty<string>() };
}

/// <summary>
/// Firma ve lead'leri Salesforce'a Account/Contact/Lead olarak aktarir.
/// Ayarlar'daki External Id alani (Egebis_External_Id__c) ile upsert yapilir,
/// boylece tekrar gonderim mukerrer kayit olusturmaz.
/// </summary>
public interface ISalesforceConnector
{
    /// <summary>
    /// Firmayi Account, kisilerini Contact olarak gonderir/gunceller.
    /// Donen SalesforceId, Account'un Id'sidir.
    /// </summary>
    Task<SalesforceSyncResult> SyncCompanyAsync(Models.Company company, CancellationToken ct = default);

    /// <summary>Lead'i Salesforce Lead nesnesi olarak gonderir/gunceller.</summary>
    Task<SalesforceSyncResult> SyncLeadAsync(Models.Lead lead, CancellationToken ct = default);

    /// <summary>Ayarlar ekranindaki "bağlantıyı test et" butonu icin: yalnizca kimlik dogrulama dener.</summary>
    Task<(bool Success, string? Error)> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// "Salesforce'a Bağlan" akisinin ilk adimi: kullaniciyi Salesforce'un kendi
    /// giris/izin ekranina yonlendirecek URL'i uretir. redirectUri, SalesforceController'daki
    /// Callback action'inin tam adresidir (Connected App'teki Callback URL ile birebir eslesmeli).
    /// codeChallenge, PKCE icin uretilen code_verifier'in SHA-256/base64url halidir
    /// (yeni External Client App'ler PKCE zorunlu tutuyor).
    /// </summary>
    Task<string?> BuildAuthorizeUrlAsync(string redirectUri, string state, string codeChallenge, CancellationToken ct = default);

    /// <summary>
    /// Callback'te alinan "code"u access/refresh token ile degistirir ve kalici olarak
    /// (Ayarlar/AppSetting) saklar. codeVerifier, BuildAuthorizeUrlAsync'e verilen
    /// codeChallenge'in kaynagi (PKCE dogrulamasi icin gerekir). Basarili olursa bir
    /// sonraki cagrilardan itibaren kullanici adi/sifre gerekmeden bu baglanti kullanilir.
    /// </summary>
    Task<(bool Success, string? Error)> ExchangeAuthorizationCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct = default);

    /// <summary>OAuth ile baglanti kurulmus mu (refresh token kayitli mi)?</summary>
    Task<bool> IsConnectedAsync(CancellationToken ct = default);

    /// <summary>Kayitli OAuth baglantisini (refresh/access token) kaldirir.</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Aktarimin ihtiyac duydugu ozel alanlari (Egebis_*) Salesforce'ta olusturur ve baglanan
    /// kullaniciya izin setiyle erisim verir. Var olanlara dokunmaz; tekrar calistirmak guvenlidir.
    /// </summary>
    Task<SalesforceSchemaResult> EnsureSchemaAsync(CancellationToken ct = default);
}
