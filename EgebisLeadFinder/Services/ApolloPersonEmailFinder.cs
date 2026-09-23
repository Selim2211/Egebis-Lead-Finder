using System.Text.Encodings.Web;
using System.Text.Json;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Apollo.io /people/match uc noktasi ile isim + domain'den kurumsal e-posta bulur.
/// 429/5xx tekrar denemesi Program.cs'teki Polly politikasi tarafindan yapilir;
/// bu sinif sadece istegi kurar ve yaniti ayristirir.
/// </summary>
public class ApolloPersonEmailFinder : IPersonEmailFinder
{
    private readonly HttpClient _http;
    private readonly ApolloOptions _options;
    private readonly ISettingsService _settings;
    private readonly ILogger<ApolloPersonEmailFinder> _logger;

    /// <summary>
    /// .NET varsayilan encoder'i "ş, ı, İ, ğ, ç, ü" gibi karakterleri \uXXXX
    /// olarak kacirir; Apollo bu formati reddediyor (bkz. ApolloPeopleSearchService).
    /// Turkce isimlerle (first_name/last_name) ayni sorunu yasamamak icin
    /// relaxed encoder kullanilir.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public ApolloPersonEmailFinder(
        HttpClient http,
        IOptions<ApolloOptions> options,
        ISettingsService settings,
        ILogger<ApolloPersonEmailFinder> logger)
    {
        _http = http;
        _options = options.Value;
        _settings = settings;
        _logger = logger;
    }

    public Task<PersonMatchResult> MatchAsync(string firstName, string lastName, string domain, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(lastName))
            return Task.FromResult(PersonMatchResult.NotFound("Soyad bulunamadığı için Apollo sorgulanmadı."));

        return SendMatchAsync(new { first_name = firstName, last_name = lastName, domain },
            $"{firstName} {lastName}", ct);
    }

    public Task<PersonMatchResult> MatchByIdAsync(string apolloId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apolloId))
            return Task.FromResult(PersonMatchResult.NotFound("Apollo kimliği yok."));

        return SendMatchAsync(new { id = apolloId }, apolloId, ct);
    }

    /// <summary>
    /// people/match ortak govdesi: hem isim+domain hem id ile ayni ucu cagirir,
    /// sadece istek govdesi degisir.
    /// </summary>
    private async Task<PersonMatchResult> SendMatchAsync(object payload, string logLabel, CancellationToken ct)
    {
        // Anahtar once Ayarlar ekranindan, orada bos ise yapilandirmadan okunur.
        var apiKey = await _settings.GetAsync(SettingKeys.ApolloApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            return PersonMatchResult.NotFound(new MissingApiKeyException("Apollo").Message);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
            request.Headers.Add("x-api-key", apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                System.Text.Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                // Yeniden denemek anlamsiz: anahtar veya scope hatali. Tek seferlik ve acik hata.
                _logger.LogWarning(
                    "Apollo yetki hatası ({Status}): anahtarın people/match scope'u açık mı kontrol edin. {Body}",
                    (int)response.StatusCode, Truncate(body, 300));
                return PersonMatchResult.NotFound($"Apollo yetki hatası ({(int)response.StatusCode}). API anahtarının scope'unu kontrol edin.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Apollo hatası {Status}: {Body}", (int)response.StatusCode, Truncate(body, 300));
                return PersonMatchResult.NotFound($"Apollo API {(int)response.StatusCode}: {Truncate(body, 150)}");
            }

            return ParseMatch(body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Apollo isteği zaman aşımına uğradı: {Label}", logLabel);
            return PersonMatchResult.NotFound("Apollo isteği zaman aşımına uğradı.");
        }
        catch (Exception ex)
        {
            // Zenginlestirme akisi opsiyoneldir; hata olsa da pipeline durmaz.
            _logger.LogWarning(ex, "Apollo eşleşmesi başarısız: {Label}", logLabel);
            return PersonMatchResult.NotFound(ex.Message);
        }
    }

    private static PersonMatchResult ParseMatch(string body)
    {
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("person", out var person) || person.ValueKind != JsonValueKind.Object)
            return PersonMatchResult.NotFound("Apollo'da eşleşen kişi bulunamadı.");

        var email = person.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;

        // Apollo, krediyle "acilmamis" e-postalar icin bu placeholder'i dondurur.
        if (string.IsNullOrWhiteSpace(email) || email.Contains("email_not_unlocked", StringComparison.OrdinalIgnoreCase))
            return PersonMatchResult.NotFound("Apollo eşleşme buldu ama e-posta açığa çıkarılamadı (kredi/plan gerekebilir).");

        string? phone = null;
        if (person.TryGetProperty("phone_numbers", out var phones) && phones.ValueKind == JsonValueKind.Array)
        {
            var first = phones.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("raw_number", out var rawPhone))
                phone = rawPhone.GetString();
        }

        var location = string.Join(", ", new[] { Text(person, "city"), Text(person, "country") }
            .Where(v => !string.IsNullOrWhiteSpace(v)));

        return new PersonMatchResult
        {
            Email = email,
            Phone = phone,
            Name = Text(person, "name"),
            FirstName = Text(person, "first_name"),
            LastName = Text(person, "last_name"),
            ProfileUrl = Text(person, "linkedin_url"),
            EmploymentStartDate = CurrentEmploymentStart(person),
            Location = string.IsNullOrWhiteSpace(location) ? null : location,
            Headline = Text(person, "headline")
        };
    }

    /// <summary>employment_history icinde current=true kaydin start_date'i ("2019-03-01").</summary>
    public static DateOnly? CurrentEmploymentStart(JsonElement person)
    {
        if (!person.TryGetProperty("employment_history", out var history) || history.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var job in history.EnumerateArray())
        {
            if (job.ValueKind != JsonValueKind.Object) continue;
            if (!job.TryGetProperty("current", out var current) || current.ValueKind != JsonValueKind.True) continue;

            var start = Text(job, "start_date");
            if (start is not null && DateOnly.TryParseExact(start.Length >= 10 ? start[..10] : start,
                    new[] { "yyyy-MM-dd", "yyyy-MM", "yyyy" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                return date;
        }

        return null;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
