using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Username-Password OAuth flow ile Salesforce REST API'ye baglanir. Kullanici
/// etkilesimi gerektirmez, bu yuzden arka planda / kullanici adina calisabilir.
/// Anahtarlar yalnizca Ayarlar ekranindan (AppSetting) okunur.
/// </summary>
public class SalesforceConnector : ISalesforceConnector
{
    private const string TokenCacheKey = "salesforce-access-token";
    private const string ExternalIdField = "Egebis_External_Id__c";

    private readonly HttpClient _http;
    private readonly SalesforceOptions _options;
    private readonly ISettingsService _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SalesforceConnector> _logger;

    public SalesforceConnector(
        HttpClient http,
        IOptions<SalesforceOptions> options,
        ISettingsService settings,
        IMemoryCache cache,
        ILogger<SalesforceConnector> logger)
    {
        _http = http;
        _options = options.Value;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SalesforceSyncResult> SyncCompanyAsync(Company company, CancellationToken ct = default)
    {
        var auth = await AuthenticateAsync(ct);
        if (auth is null) return SalesforceSyncResult.Failed(MissingConfigMessage);

        var result = await UpsertWithSchemaAsync(
            auth, "Account", $"company-{company.Id}", SalesforceRecordMapper.Account(company), ct);
        if (!result.Success) return result;

        // Kisiler: Account Id'si elde edilince Contact olarak da gonderilir.
        // Bir kisi hata verirse digerleri devam eder; Company senkronu yine basarili sayilir.
        foreach (var contact in company.Contacts.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
        {
            var contactResult = await UpsertWithSchemaAsync(
                auth, "Contact", $"contact-{contact.Id}", SalesforceRecordMapper.Contact(contact, result.SalesforceId!), ct);
            if (!contactResult.Success)
                _logger.LogWarning("Salesforce Contact senkronu basarisiz: {Error}", contactResult.Error);
        }

        return result;
    }

    public async Task<SalesforceSyncResult> SyncLeadAsync(Lead lead, CancellationToken ct = default)
    {
        var auth = await AuthenticateAsync(ct);
        if (auth is null) return SalesforceSyncResult.Failed(MissingConfigMessage);

        return await UpsertWithSchemaAsync(auth, "Lead", $"lead-{lead.Id}", SalesforceRecordMapper.Lead(lead), ct);
    }

    // --- Ozel alan kurulumu (Tooling API) ---

    private const string PermissionSetName = "Egebis_Lead_Finder";

    private record FieldSpec(string Object, string Name, object Metadata)
    {
        public string FullName => $"{Object}.{Name}";
    }

    private static readonly object ExternalIdMeta =
        new { label = "Egebis External Id", type = "Text", length = 100, externalId = true, unique = true, caseSensitive = false };
    private static readonly object ScoreMeta = new { label = "Egebis Skor", type = "Number", precision = 3, scale = 0 };
    private static readonly object SignalMeta = new { label = "Egebis Sinyal", type = "Text", length = 20 };
    private static readonly object SummaryMeta = new { label = "Egebis AI Özeti", type = "LongTextArea", length = 32768, visibleLines = 5 };

    private static object LongTextMeta(string label) => new { label, type = "LongTextArea", length = 32768, visibleLines = 5 };
    private static readonly object SalesApproachMeta = LongTextMeta("Egebis Satış Önerisi");
    private static readonly object OpportunitiesMeta = LongTextMeta("Egebis Fırsatlar");
    private static readonly object EmailStatusMeta = new { label = "Egebis E-posta Durumu", type = "Text", length = 30 };
    private static readonly object NaceMeta = new { label = "Egebis NACE", type = "Text", length = 10 };

    /// <summary>Aktarimin ihtiyac duydugu tum ozel alanlar. Yeni alan eklenirse buraya yazilir.</summary>
    private static readonly FieldSpec[] RequiredFields =
    {
        new("Account", "Egebis_External_Id__c", ExternalIdMeta),
        new("Account", "Egebis_Score__c", ScoreMeta),
        new("Account", "Egebis_Signal__c", SignalMeta),
        new("Account", "Egebis_AI_Summary__c", SummaryMeta),
        new("Account", "Egebis_Country__c", new { label = "Egebis Ülke", type = "Text", length = 80 }),
        new("Account", "Egebis_Industry__c", new { label = "Egebis Sektör", type = "Text", length = 255 }),
        new("Account", "Egebis_Stage__c", new { label = "Egebis Aşama", type = "Text", length = 40 }),
        new("Account", "Egebis_Confidence__c", new { label = "Egebis Analiz Güveni", type = "Number", precision = 3, scale = 0 }),
        new("Account", "Egebis_Analyzed_At__c", new { label = "Egebis Analiz Tarihi", type = "DateTime" }),
        new("Account", "Egebis_Sales_Approach__c", SalesApproachMeta),
        new("Account", "Egebis_Opportunities__c", OpportunitiesMeta),
        new("Account", "Egebis_Financials__c", LongTextMeta("Egebis Finans ve Büyüklük")),
        new("Account", "Egebis_Management__c", LongTextMeta("Egebis Yönetim")),
        new("Account", "Egebis_Technology__c", LongTextMeta("Egebis Teknoloji")),
        new("Account", "Egebis_Risks__c", LongTextMeta("Egebis Riskler")),
        new("Account", "Egebis_News__c", LongTextMeta("Egebis Haberler")),
        new("Contact", "Egebis_External_Id__c", ExternalIdMeta),
        new("Contact", "Egebis_Email_Status__c", EmailStatusMeta),
        new("Lead", "Egebis_Email_Status__c", EmailStatusMeta),
        new("Account", "Egebis_NACE__c", NaceMeta),
        new("Lead", "Egebis_NACE__c", NaceMeta),
        new("Lead", "Egebis_External_Id__c", ExternalIdMeta),
        new("Lead", "Egebis_Score__c", ScoreMeta),
        new("Lead", "Egebis_Signal__c", SignalMeta),
        new("Lead", "Egebis_AI_Summary__c", SummaryMeta),
        new("Lead", "Egebis_Status__c", new { label = "Egebis Durum", type = "Text", length = 40 }),
        new("Lead", "Egebis_Last_Email_At__c", new { label = "Egebis Son Mail", type = "DateTime" }),
        new("Lead", "Egebis_Sales_Approach__c", SalesApproachMeta),
        new("Lead", "Egebis_Opportunities__c", OpportunitiesMeta)
    };

    public async Task<SalesforceSchemaResult> EnsureSchemaAsync(CancellationToken ct = default)
    {
        var auth = await AuthenticateAsync(ct);
        if (auth is null) return SalesforceSchemaResult.Failed(MissingConfigMessage);
        return await EnsureSchemaAsync(auth, ct);
    }

    /// <summary>
    /// Eksik ozel alanlari olusturur ve "Egebis Lead Finder" izin setiyle baglanan
    /// kullaniciya okuma/yazma yetkisi verir. Tekrar calistirmak guvenlidir: var olanlara dokunmaz.
    /// </summary>
    private async Task<SalesforceSchemaResult> EnsureSchemaAsync(AuthContext auth, CancellationToken ct)
    {
        // 1) Hangi alanlar zaten var? Tooling API, FLS'ten bagimsiz olarak tum ozel alanlari gorur
        // (describe ise yalnizca kullanicinin erisebildiklerini dondurur).
        var objects = string.Join(",", RequiredFields.Select(f => $"'{f.Object}'").Distinct());
        var (qStatus, qBody) = await ApiAsync(auth, HttpMethod.Get,
            "tooling/query/?q=" + Uri.EscapeDataString($"SELECT DeveloperName, TableEnumOrId FROM CustomField WHERE TableEnumOrId IN ({objects})"),
            null, ct);
        if (!IsSuccess(qStatus)) return SalesforceSchemaResult.Failed("Alan listesi okunamadı: " + (ExtractErrorMessage(qBody) ?? qStatus.ToString()));

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var doc = JsonDocument.Parse(qBody))
            foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
                existing.Add($"{r.GetProperty("TableEnumOrId").GetString()}.{r.GetProperty("DeveloperName").GetString()}__c");

        // 2) Eksikleri olustur.
        var created = new List<string>();
        foreach (var field in RequiredFields.Where(f => !existing.Contains(f.FullName)))
        {
            var (status, body) = await ApiAsync(auth, HttpMethod.Post, "tooling/sobjects/CustomField",
                new { FullName = field.FullName, Metadata = field.Metadata }, ct);
            if (!IsSuccess(status))
                return SalesforceSchemaResult.Failed($"{field.FullName} oluşturulamadı: {ExtractErrorMessage(body) ?? status.ToString()}", created);
            created.Add(field.FullName);
        }

        // 3) Izin seti: yeni alanlar varsayilan olarak hicbir kullaniciya gorunmez.
        var psId = await QuerySingleIdAsync(auth, $"SELECT Id FROM PermissionSet WHERE Name = '{PermissionSetName}'", ct);
        if (psId is null)
        {
            var (status, body) = await ApiAsync(auth, HttpMethod.Post, "sobjects/PermissionSet",
                new { Name = PermissionSetName, Label = "Egebis Lead Finder" }, ct);
            if (!IsSuccess(status))
                return SalesforceSchemaResult.Failed("İzin seti oluşturulamadı: " + (ExtractErrorMessage(body) ?? status.ToString()), created);
            psId = ReadId(body);
        }

        var (fpStatus, fpBody) = await ApiAsync(auth, HttpMethod.Get,
            "query/?q=" + Uri.EscapeDataString($"SELECT Field FROM FieldPermissions WHERE ParentId = '{psId}'"), null, ct);
        var permitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (IsSuccess(fpStatus))
            using (var doc = JsonDocument.Parse(fpBody))
                foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
                    permitted.Add(r.GetProperty("Field").GetString() ?? "");

        foreach (var field in RequiredFields.Where(f => !permitted.Contains(f.FullName)))
        {
            var (status, body) = await ApiAsync(auth, HttpMethod.Post, "sobjects/FieldPermissions", new
            {
                ParentId = psId,
                SobjectType = field.Object,
                Field = field.FullName,
                PermissionsRead = true,
                PermissionsEdit = true
            }, ct);
            if (!IsSuccess(status))
                return SalesforceSchemaResult.Failed($"{field.FullName} için yetki verilemedi: {ExtractErrorMessage(body) ?? status.ToString()}", created);
        }

        // 4) Izin setini baglanan kullaniciya ata.
        var userId = await CurrentUserIdAsync(auth, ct);
        if (userId is null) return SalesforceSchemaResult.Failed("Bağlı Salesforce kullanıcısı bulunamadı.", created);

        var assigned = await QuerySingleIdAsync(auth,
            $"SELECT Id FROM PermissionSetAssignment WHERE AssigneeId = '{userId}' AND PermissionSetId = '{psId}'", ct);
        if (assigned is null)
        {
            var (status, body) = await ApiAsync(auth, HttpMethod.Post, "sobjects/PermissionSetAssignment",
                new { AssigneeId = userId, PermissionSetId = psId }, ct);
            if (!IsSuccess(status))
                return SalesforceSchemaResult.Failed("İzin seti kullanıcıya atanamadı: " + (ExtractErrorMessage(body) ?? status.ToString()), created);
        }

        // 5) Alanlari kayit sayfalarina ekle. Basarisiz olsa da veri aktarimi calisir; uyari olarak doner.
        var (layouts, layoutWarning) = await EnsureLayoutsAsync(auth, ct);

        return SalesforceSchemaResult.Ok(created, layouts, layoutWarning);
    }

    // --- Sayfa duzenleri (Tooling API Layout) ---

    private const string LayoutSectionLabel = "Egebis Lead Finder";

    /// <summary>Kayit sayfasinda gosterilecek alanlar. External Id teknik oldugu icin gosterilmez; Contact'ta baska alan yok.</summary>
    private static readonly (string Object, string[] Fields)[] LayoutFields =
    {
        ("Account", new[]
        {
            "Egebis_Score__c", "Egebis_Signal__c", "Egebis_Stage__c", "Egebis_Confidence__c",
            "Egebis_Analyzed_At__c", "Egebis_Country__c", "Egebis_Industry__c", "Egebis_NACE__c", "Egebis_AI_Summary__c",
            "Egebis_Sales_Approach__c", "Egebis_Opportunities__c", "Egebis_Financials__c",
            "Egebis_Management__c", "Egebis_Technology__c", "Egebis_Risks__c", "Egebis_News__c"
        }),
        ("Lead", new[]
        {
            "Egebis_Score__c", "Egebis_Signal__c", "Egebis_Status__c", "Egebis_Email_Status__c", "Egebis_NACE__c",
            "Egebis_Last_Email_At__c", "Egebis_AI_Summary__c", "Egebis_Sales_Approach__c", "Egebis_Opportunities__c"
        }),
        ("Contact", new[] { "Egebis_Email_Status__c" })
    };

    /// <summary>
    /// Nesnenin tum sayfa duzenlerine "Egebis Lead Finder" bolumunu ekler. Duzende zaten
    /// (herhangi bir bolumde) bulunan alana dokunulmaz; bolum varsa yalnizca eksikler eklenir.
    /// Kullanicinin duzende yaptigi degisiklikler korunur.
    /// </summary>
    private async Task<(List<string> Updated, string? Warning)> EnsureLayoutsAsync(AuthContext auth, CancellationToken ct)
    {
        var updated = new List<string>();
        var problems = new List<string>();

        foreach (var (objectName, fields) in LayoutFields)
        {
            var (qStatus, qBody) = await ApiAsync(auth, HttpMethod.Get,
                "tooling/query/?q=" + Uri.EscapeDataString($"SELECT Id, Name FROM Layout WHERE TableEnumOrId = '{objectName}'"), null, ct);
            if (!IsSuccess(qStatus))
            {
                problems.Add($"{objectName} düzenleri okunamadı ({ExtractErrorMessage(qBody) ?? qStatus.ToString()})");
                continue;
            }

            var layouts = new List<(string Id, string Name)>();
            using (var doc = JsonDocument.Parse(qBody))
                foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
                    layouts.Add((r.GetProperty("Id").GetString()!, r.GetProperty("Name").GetString() ?? objectName));

            foreach (var (layoutId, layoutName) in layouts)
            {
                var outcome = await AddFieldsToLayoutAsync(auth, layoutId, fields, ct);
                if (outcome == Updated) updated.Add($"{objectName}: {layoutName}");
                else if (outcome != NothingToDo) problems.Add($"{objectName}: {layoutName} ({outcome})");
            }
        }

        return (updated, problems.Count == 0 ? null : "Bazı sayfa düzenleri güncellenemedi: " + string.Join("; ", problems));
    }

    private const string NothingToDo = "__nothing__";
    private const string Updated = "__updated__";

    /// <returns>NothingToDo, Updated ya da hata mesaji.</returns>
    private async Task<string> AddFieldsToLayoutAsync(AuthContext auth, string layoutId, string[] fields, CancellationToken ct)
    {
        var (gStatus, gBody) = await ApiAsync(auth, HttpMethod.Get, $"tooling/sobjects/Layout/{layoutId}", null, ct);
        if (!IsSuccess(gStatus)) return ExtractErrorMessage(gBody) ?? gStatus.ToString();

        if (System.Text.Json.Nodes.JsonNode.Parse(gBody)?["Metadata"] is not System.Text.Json.Nodes.JsonObject metadata)
            return "düzen içeriği okunamadı";

        if (metadata["layoutSections"] is not System.Text.Json.Nodes.JsonArray sections)
            metadata["layoutSections"] = sections = new System.Text.Json.Nodes.JsonArray();

        // Duzende (hangi bolumde olursa olsun) zaten bulunan alanlar.
        var present = sections
            .SelectMany(s => s?["layoutColumns"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray())
            .SelectMany(c => c?["layoutItems"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray())
            .Select(i => i?["field"]?.GetValue<string>())
            .Where(f => f is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = fields.Where(f => !present.Contains(f)).ToList();
        if (missing.Count == 0) return NothingToDo;

        System.Text.Json.Nodes.JsonObject Item(string field) => new() { ["behavior"] = "Edit", ["field"] = field };

        var ourSection = sections.OfType<System.Text.Json.Nodes.JsonObject>()
            .FirstOrDefault(s => s["label"]?.GetValue<string>() == LayoutSectionLabel);

        if (ourSection?["layoutColumns"]?.AsArray().FirstOrDefault() is System.Text.Json.Nodes.JsonObject firstColumn)
        {
            if (firstColumn["layoutItems"] is not System.Text.Json.Nodes.JsonArray items)
                firstColumn["layoutItems"] = items = new System.Text.Json.Nodes.JsonArray();
            foreach (var f in missing) items.Add(Item(f));
        }
        else
        {
            var section = new System.Text.Json.Nodes.JsonObject
            {
                ["customLabel"] = true,
                ["detailHeading"] = true,
                ["editHeading"] = true,
                ["label"] = LayoutSectionLabel,
                ["style"] = "OneColumn",
                ["layoutColumns"] = new System.Text.Json.Nodes.JsonArray(
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["layoutItems"] = new System.Text.Json.Nodes.JsonArray(missing.Select(f => (System.Text.Json.Nodes.JsonNode)Item(f)).ToArray())
                    })
            };
            // Ilk bolumun (genelde ana bilgiler) hemen altina: kayit acilinca kaydirmadan gorunsun.
            sections.Insert(Math.Min(1, sections.Count), section);
        }

        var patch = new System.Text.Json.Nodes.JsonObject { ["Metadata"] = metadata.DeepClone() };
        var (pStatus, pBody) = await ApiAsync(auth, HttpMethod.Patch, $"tooling/sobjects/Layout/{layoutId}", patch, ct);
        return IsSuccess(pStatus) ? Updated : ExtractErrorMessage(pBody) ?? pStatus.ToString();
    }

    /// <summary>
    /// Upsert'i dener; hata eksik ozel alandan kaynaklaniyorsa alanlari bir kez kurar ve
    /// tekrar dener. Boylece kullanici alanlari elle olusturmak zorunda kalmaz.
    /// </summary>
    private async Task<SalesforceSyncResult> UpsertWithSchemaAsync(
        AuthContext auth, string objectType, string externalId, Dictionary<string, object?> fields, CancellationToken ct)
    {
        var result = await UpsertAsync(auth, objectType, externalId, fields, ct);
        if (result.Success || !IsMissingFieldError(result.Error)) return result;

        _logger.LogInformation("Salesforce ozel alanlari eksik; kurulum yapiliyor ({Error})", result.Error);
        var schema = await EnsureSchemaAsync(auth, ct);
        if (!schema.Success) return SalesforceSyncResult.Failed("Salesforce alanları kurulamadı: " + schema.Error);

        return await UpsertAsync(auth, objectType, externalId, fields, ct);
    }

    /// <summary>NOT_FOUND (external id alani yok) veya INVALID_FIELD (Egebis_ alani yok/gorunmuyor).</summary>
    private static bool IsMissingFieldError(string? error) =>
        error is not null && error.Contains("Egebis_", StringComparison.OrdinalIgnoreCase)
        && (error.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase) || error.Contains("INVALID_FIELD", StringComparison.OrdinalIgnoreCase));

    private async Task<string?> CurrentUserIdAsync(AuthContext auth, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{auth.InstanceUrl}/services/oauth2/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        try
        {
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("user_id", out var id) ? id.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Salesforce userinfo alinamadi");
            return null;
        }
    }

    private async Task<string?> QuerySingleIdAsync(AuthContext auth, string soql, CancellationToken ct)
    {
        var (status, body) = await ApiAsync(auth, HttpMethod.Get, "query/?q=" + Uri.EscapeDataString(soql), null, ct);
        if (!IsSuccess(status)) return null;
        using var doc = JsonDocument.Parse(body);
        var records = doc.RootElement.GetProperty("records");
        return records.GetArrayLength() > 0 ? records[0].GetProperty("Id").GetString() : null;
    }

    /// <summary>/services/data/{versiyon}/ altina istek atar; govde JSON'a cevrilir.</summary>
    private async Task<(System.Net.HttpStatusCode Status, string Body)> ApiAsync(
        AuthContext auth, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{auth.InstanceUrl}/services/data/{_options.ApiVersion}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            using var response = await _http.SendAsync(request, cts.Token);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Salesforce API istegi basarisiz: {Method} {Path}", method, path);
            return (System.Net.HttpStatusCode.ServiceUnavailable, ex.Message);
        }
    }

    private static bool IsSuccess(System.Net.HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static string? ReadId(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    // --- Kimlik dogrulama ---

    private record AuthContext(string AccessToken, string InstanceUrl);

    private async Task<AuthContext?> AuthenticateAsync(CancellationToken ct)
    {
        var (context, _) = await AuthenticateWithReasonAsync(ct);
        return context;
    }

    public async Task<(bool Success, string? Error)> TestConnectionAsync(CancellationToken ct = default)
    {
        _cache.Remove(TokenCacheKey); // test her zaman taze dener
        var (context, error) = await AuthenticateWithReasonAsync(ct);
        return context is null ? (false, error ?? "Bilinmeyen hata") : (true, null);
    }

    /// <summary>
    /// Refresh token rotasyonu acik org'larda her yenileme eski refresh token'i iptal eder.
    /// Iki istek ayni anda yenilerse ikincisi iptal edilmis token'la reddedilir ve baglanti
    /// duser; bu yuzden yenileme uygulama genelinde tek seferde bire indirilir. Servis
    /// typed HttpClient oldugu icin her istekte yeni ornek olusur; kilit statiktir.
    /// </summary>
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private async Task<(AuthContext? Context, string? Error)> AuthenticateWithReasonAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(TokenCacheKey, out AuthContext? cached) && cached is not null)
            return (cached, null);

        await RefreshLock.WaitAsync(ct);
        try
        {
            // Kilidi beklerken baska bir istek token'i yenilemis olabilir.
            if (_cache.TryGetValue(TokenCacheKey, out cached) && cached is not null)
                return (cached, null);

            // Once OAuth baglantisi (refresh token) denenir: "Salesforce'a Bağlan" akisinda
            // kurulmustur, kullanici sifresi gerektirmez ve cok kiracili kullanima uygundur.
            var refreshToken = await _settings.GetAsync(SettingKeys.SalesforceRefreshToken, ct);
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var (refreshed, refreshError) = await RefreshAccessTokenAsync(refreshToken, ct);
                if (refreshed is not null) return (refreshed, null);
                // Refresh basarisiz (ör. iptal edilmis izin): kullanici adi/sifreye dus, o da
                // yoksa asagidaki mesaj donsun.
                if (string.IsNullOrWhiteSpace(await _settings.GetAsync(SettingKeys.SalesforceUsername, ct)))
                    return (null, refreshError);
            }

            return await AuthenticateWithPasswordAsync(ct);
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<(AuthContext? Context, string? Error)> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        var consumerKey = await _settings.GetAsync(SettingKeys.SalesforceConsumerKey, ct);
        var consumerSecret = await _settings.GetAsync(SettingKeys.SalesforceConsumerSecret, ct);
        var loginUrl = await _settings.GetAsync(SettingKeys.SalesforceLoginUrl, ct);
        loginUrl = string.IsNullOrWhiteSpace(loginUrl) ? _options.LoginUrl : loginUrl;

        if (string.IsNullOrWhiteSpace(consumerKey) || string.IsNullOrWhiteSpace(consumerSecret))
            return (null, MissingConfigMessage);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = consumerKey,
            ["client_secret"] = consumerSecret,
            ["refresh_token"] = refreshToken
        };

        var (context, error) = await PostTokenRequestAsync(loginUrl, form, ct);

        // Refresh_token cevabinda instance_url her zaman gelmeyebilir; kayitli degere dus.
        if (context is null && error is not null
            && (error.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)))
        {
            // Kullanici Salesforce tarafinda izni iptal etmis olabilir: kayitli refresh token temizlenir.
            await _settings.SetManyAsync(new Dictionary<string, string?> { [SettingKeys.SalesforceRefreshToken] = null }, ct);
        }

        return (context, error);
    }

    private async Task<(AuthContext? Context, string? Error)> AuthenticateWithPasswordAsync(CancellationToken ct)
    {
        var consumerKey = await _settings.GetAsync(SettingKeys.SalesforceConsumerKey, ct);
        var consumerSecret = await _settings.GetAsync(SettingKeys.SalesforceConsumerSecret, ct);
        var username = await _settings.GetAsync(SettingKeys.SalesforceUsername, ct);
        var password = await _settings.GetAsync(SettingKeys.SalesforcePassword, ct);
        var token = await _settings.GetAsync(SettingKeys.SalesforceSecurityToken, ct);
        var loginUrl = await _settings.GetAsync(SettingKeys.SalesforceLoginUrl, ct);
        loginUrl = string.IsNullOrWhiteSpace(loginUrl) ? _options.LoginUrl : loginUrl;

        if (string.IsNullOrWhiteSpace(consumerKey) || string.IsNullOrWhiteSpace(consumerSecret)
            || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (null, MissingConfigMessage);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = consumerKey,
            ["client_secret"] = consumerSecret,
            ["username"] = username,
            ["password"] = password + (token ?? string.Empty)
        };

        return await PostTokenRequestAsync(loginUrl, form, ct);
    }

    /// <summary>oauth2/token ucuna form-urlencoded istek atar, basariliysa AuthContext'i onbellege alir.</summary>
    private async Task<(AuthContext? Context, string? Error)> PostTokenRequestAsync(
        string loginUrl, Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{loginUrl}/services/oauth2/token")
        {
            Content = new FormUrlEncodedContent(form)
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Salesforce kimlik dogrulama istegi basarisiz");
            return (null, "Salesforce'a bağlanılamadı: " + ex.Message);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Salesforce OAuth hatasi {Status}: {Body}", response.StatusCode, body);
            return (null, ExtractOAuthErrorMessage(body) ?? $"HTTP {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var accessToken = doc.RootElement.GetProperty("access_token").GetString();
        var instanceUrl = doc.RootElement.TryGetProperty("instance_url", out var iu) ? iu.GetString() : null;
        if (string.IsNullOrWhiteSpace(instanceUrl))
            instanceUrl = await _settings.GetAsync(SettingKeys.SalesforceInstanceUrl, ct);

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(instanceUrl))
            return (null, "Salesforce beklenmeyen bir yanıt döndü.");

        // Refresh token rotasyonu: Salesforce yeni bir refresh token dondurduyse eskisi artik
        // gecersizdir; hemen kaydedilmezse bir sonraki yenilemede baglanti kopar.
        if (form.GetValueOrDefault("grant_type") == "refresh_token"
            && doc.RootElement.TryGetProperty("refresh_token", out var rotated)
            && rotated.GetString() is { Length: > 0 } newRefreshToken
            && newRefreshToken != form["refresh_token"])
        {
            await _settings.SetManyAsync(new Dictionary<string, string?>
            {
                [SettingKeys.SalesforceRefreshToken] = newRefreshToken,
                [SettingKeys.SalesforceInstanceUrl] = instanceUrl
            }, ct);
        }

        var context = new AuthContext(accessToken, instanceUrl);
        // Token'in gercek suresi ~2 saat; guvenli tarafta kalmak icin 25 dakika tutulur.
        _cache.Set(TokenCacheKey, context, TimeSpan.FromMinutes(25));
        return (context, null);
    }

    // --- OAuth "Salesforce'a Bağlan" akisi (Authorization Code flow) ---

    private const string OAuthScope = "api refresh_token offline_access";

    public async Task<string?> BuildAuthorizeUrlAsync(string redirectUri, string state, string codeChallenge, CancellationToken ct = default)
    {
        var consumerKey = await _settings.GetAsync(SettingKeys.SalesforceConsumerKey, ct);
        if (string.IsNullOrWhiteSpace(consumerKey)) return null;

        var loginUrl = await _settings.GetAsync(SettingKeys.SalesforceLoginUrl, ct);
        loginUrl = string.IsNullOrWhiteSpace(loginUrl) ? _options.LoginUrl : loginUrl;

        var qs = string.Join("&", new[]
        {
            "response_type=code",
            $"client_id={Uri.EscapeDataString(consumerKey)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"state={Uri.EscapeDataString(state)}",
            $"scope={Uri.EscapeDataString(OAuthScope)}",
            // Yeni External Client App'ler PKCE zorunlu tutuyor (S256).
            $"code_challenge={Uri.EscapeDataString(codeChallenge)}",
            "code_challenge_method=S256"
        });

        return $"{loginUrl}/services/oauth2/authorize?{qs}";
    }

    public async Task<(bool Success, string? Error)> ExchangeAuthorizationCodeAsync(
        string code, string redirectUri, string codeVerifier, CancellationToken ct = default)
    {
        var consumerKey = await _settings.GetAsync(SettingKeys.SalesforceConsumerKey, ct);
        var consumerSecret = await _settings.GetAsync(SettingKeys.SalesforceConsumerSecret, ct);
        var loginUrl = await _settings.GetAsync(SettingKeys.SalesforceLoginUrl, ct);
        loginUrl = string.IsNullOrWhiteSpace(loginUrl) ? _options.LoginUrl : loginUrl;

        if (string.IsNullOrWhiteSpace(consumerKey) || string.IsNullOrWhiteSpace(consumerSecret))
            return (false, "Önce Consumer Key ve Consumer Secret'ı kaydedin.");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = consumerKey,
            ["client_secret"] = consumerSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{loginUrl}/services/oauth2/token")
        {
            Content = new FormUrlEncodedContent(form)
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Salesforce authorization_code degisimi basarisiz");
            return (false, "Salesforce'a bağlanılamadı: " + ex.Message);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Salesforce authorization_code hatasi {Status}: {Body}", response.StatusCode, body);
            return (false, ExtractOAuthErrorMessage(body) ?? $"HTTP {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var accessToken = doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var instanceUrl = doc.RootElement.TryGetProperty("instance_url", out var iu) ? iu.GetString() : null;

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(instanceUrl))
            return (false, "Salesforce beklenmeyen bir yanıt döndü (refresh token alınamadı).");

        await _settings.SetManyAsync(new Dictionary<string, string?>
        {
            [SettingKeys.SalesforceRefreshToken] = refreshToken,
            [SettingKeys.SalesforceInstanceUrl] = instanceUrl
        }, ct);

        _cache.Set(TokenCacheKey, new AuthContext(accessToken, instanceUrl), TimeSpan.FromMinutes(25));
        return (true, null);
    }

    public async Task<bool> IsConnectedAsync(CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(await _settings.GetAsync(SettingKeys.SalesforceRefreshToken, ct));

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _cache.Remove(TokenCacheKey);
        await _settings.SetManyAsync(new Dictionary<string, string?>
        {
            [SettingKeys.SalesforceRefreshToken] = null,
            [SettingKeys.SalesforceInstanceUrl] = null
        }, ct);
    }

    private static string? ExtractOAuthErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var desc = doc.RootElement.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            var code = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return string.Join(": ", new[] { code, desc }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        catch (JsonException)
        {
            return body.Length > 300 ? body[..300] : body;
        }
    }

    // --- Upsert (External Id ile kayit olustur/guncelle) ---

    private async Task<SalesforceSyncResult> UpsertAsync(
        AuthContext auth, string objectType, string externalId, Dictionary<string, object?> fields, CancellationToken ct)
    {
        var url = $"{auth.InstanceUrl}/services/data/{_options.ApiVersion}/sobjects/{objectType}/{ExternalIdField}/{Uri.EscapeDataString(externalId)}";

        using var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(fields), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Salesforce {ObjectType} upsert istegi basarisiz", objectType);
            return SalesforceSyncResult.Failed("Salesforce'a bağlanılamadı: " + ex.Message);
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        // 401: token gecersiz olmus olabilir (ör. sifre degisti) - onbellegi temizle.
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _cache.Remove(TokenCacheKey);
            return SalesforceSyncResult.Failed("Salesforce oturumu geçersiz; Ayarlar'daki bilgileri kontrol edin.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = ExtractErrorMessage(body) ?? $"HTTP {(int)response.StatusCode}";
            _logger.LogWarning("Salesforce {ObjectType} upsert hatasi {Status}: {Body}", objectType, response.StatusCode, body);
            return SalesforceSyncResult.Failed(message);
        }

        // 201 Created: govdede yeni kaydin Id'si doner. 204 No Content: mevcut kayit
        // guncellendi, Id'yi ogrenmek icin ayrica sorgulamak gerekir.
        if (response.StatusCode == System.Net.HttpStatusCode.Created && !string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("id", out var idProp))
                return SalesforceSyncResult.Ok(idProp.GetString() ?? "");
        }

        var queried = await QueryIdAsync(auth, objectType, externalId, ct);
        return queried is null
            ? SalesforceSyncResult.Failed("Kayıt güncellendi ama Id sorgulanamadı.")
            : SalesforceSyncResult.Ok(queried);
    }

    private async Task<string?> QueryIdAsync(AuthContext auth, string objectType, string externalId, CancellationToken ct)
    {
        var soql = Uri.EscapeDataString($"SELECT Id FROM {objectType} WHERE {ExternalIdField} = '{externalId.Replace("'", "\\'")}' LIMIT 1");
        var url = $"{auth.InstanceUrl}/services/data/{_options.ApiVersion}/query/?q={soql}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        try
        {
            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var records = doc.RootElement.GetProperty("records");
            return records.GetArrayLength() > 0 ? records[0].GetProperty("Id").GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Salesforce Id sorgusu basarisiz");
            return null;
        }
    }

    // --- Yardimcilar ---

    private const string MissingConfigMessage = "Salesforce bağlantı bilgileri eksik. Ayarlar sayfasından girin.";

    private static string? ExtractErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var first = doc.RootElement[0];
                var code = first.TryGetProperty("errorCode", out var c) ? c.GetString() : null;
                var msg = first.TryGetProperty("message", out var m) ? m.GetString() : null;
                return string.Join(": ", new[] { code, msg }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
        }
        catch (JsonException)
        {
            // govde JSON degil; ham metni kisaltarak dondur.
        }
        return body.Length > 300 ? body[..300] : body;
    }
}
