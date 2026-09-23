using System.Text.Encodings.Web;
using System.Text.Json;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Apollo.io /mixed_people/search ile firma domaininden karar verici arar.
/// Unvan filtresi sunucu tarafinda uygulanir (person_titles), boylece Ayarlar
/// ekranindaki unvanlar dogrudan sorguya gider ve donen unvanlar yapisal,
/// kirpilmamis olur.
/// </summary>
public class ApolloPeopleSearchService : IPeopleSearchService
{
    private readonly HttpClient _http;
    private readonly ApolloOptions _options;
    private readonly ISettingsService _settings;
    private readonly ILogger<ApolloPeopleSearchService> _logger;

    /// <summary>
    /// .NET'in varsayilan JSON encoder'i "ü, ş, ı, İ, ğ, ç" gibi ASCII-disi
    /// karakterleri \uXXXX olarak kacirir (XSS-safe varsayilan). Apollo'nun
    /// arama ucu bu escape formatini kabul etmiyor ve "IT Müdürü" gibi Turkce
    /// bir unvan person_titles icinde gecerse TUM istegi 400 ile reddediyor —
    /// sessizce LinkedIn yedegine dusuluyor gibi gorunuyordu. Cozum: relaxed
    /// encoder ile Unicode karakterleri ham UTF-8 olarak birak.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Apollo'nun person_titles ucundaki sabit terim siniri (asilirsa 422 doner).
    /// Apollo'nun kendi hata mesaji "150" ve "100" gibi farkli sayilar verebiliyor
    /// (include+exclude toplami muhtemelen 100); guvenli tarafta kalmak icin 90.
    /// </summary>
    private const int MaxPersonTitleTerms = 90;

    public ApolloPeopleSearchService(
        HttpClient http,
        IOptions<ApolloOptions> options,
        ISettingsService settings,
        ILogger<ApolloPeopleSearchService> logger)
    {
        _http = http;
        _options = options.Value;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PeopleSearchResult> SearchAsync(
        string domain,
        IReadOnlyList<string> titleKeywords,
        int maxResults,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return PeopleSearchResult.NotConfigured("Firma alan adı yok, Apollo araması yapılamaz.");

        // Anahtar once Ayarlar ekranindan, orada bos ise yapilandirmadan okunur.
        var apiKey = await _settings.GetAsync(SettingKeys.ApolloApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            return PeopleSearchResult.NotConfigured(new MissingApiKeyException("Apollo").Message);

        // Genis ag: sonucu sadece maxResults'a (ör. 3) gore sinirlamak, en iyi
        // degil ILK gelen adaylari almak demekti. api_search 0 kredi oldugu
        // icin daha genis bir aday havuzu cekip unvan puanina gore en iyisini
        // secmek (bkz. HybridContactEnrichmentService) bedava.
        var fetchSize = Math.Clamp(Math.Max(maxResults, 25), 1, 100);

        // Apollo person_titles'ta sabit bir terim siniri koyuyor (asilirsa 422
        // doner, TUM istegi kirar). Bu siniri asmak icin listeyi parcalara
        // bolup HER PARCA icin ayri (yine kredisiz) bir istek atariz; sonuclari
        // ApolloId'ye gore tekillestirerek birlestiririz. Boylece 100'den fazla
        // unvan da — kredi maliyeti olmadan, sadece birkac ekstra HTTP istegiyle —
        // taranabilir.
        var titleBatches = titleKeywords.Count > 0
            ? titleKeywords.Chunk(MaxPersonTitleTerms).ToList()
            : new List<string[]> { Array.Empty<string>() };

        var merged = new Dictionary<string, PersonCandidate>();
        string? lastError = null;
        var anySucceeded = false;

        foreach (var batch in titleBatches)
        {
            ct.ThrowIfCancellationRequested();

            var outcome = await QueryBatchAsync(domain, batch, apiKey, fetchSize, ct);

            if (outcome.Unauthorized)
            {
                // Arama ucu Apollo'da ucretli plana/scope'a bagli olabilir. Anahtar
                // sorunuysa her parca ayni sonucu verir; tekrar denemenin anlami
                // yok, hemen "bu yol kapali" bilgisiyle yedek akisa gecilmeli.
                return PeopleSearchResult.NotConfigured(outcome.Error!);
            }

            if (outcome.Error is not null)
            {
                lastError = outcome.Error;
                continue;
            }

            anySucceeded = true;
            foreach (var person in outcome.People)
                // Ayni kisi (ör. genel unvanlara uyan biri) birden fazla parcada
                // cikabilir; ApolloId ile tekillestirilir, id yoksa atlanir.
                if (!string.IsNullOrWhiteSpace(person.ApolloId))
                    merged[person.ApolloId!] = person;
        }

        if (!anySucceeded)
            return PeopleSearchResult.Failed(lastError ?? "Apollo arama başarısız.");

        if (titleBatches.Count > 1)
            _logger.LogInformation(
                "Apollo unvan listesi {Batches} parcaya bolundu ({Terms} terim), {Found} benzersiz aday bulundu: {Domain}",
                titleBatches.Count, titleKeywords.Count, merged.Count, domain);

        return new PeopleSearchResult { People = merged.Values.ToList() };
    }

    /// <summary>Tek bir person_titles parcasi icin api_search istegi atar; hata durumunda exception firlatmaz.</summary>
    private async Task<BatchOutcome> QueryBatchAsync(
        string domain, IReadOnlyList<string> titles, string apiKey, int fetchSize, CancellationToken ct)
    {
        try
        {
            var payload = new Dictionary<string, object>
            {
                ["q_organization_domains_list"] = new[] { domain },
                ["page"] = 1,
                ["per_page"] = fetchSize,
                ["include_similar_titles"] = _options.IncludeSimilarTitles
            };

            // Unvan filtresi opsiyoneldir: bos birakilirsa firmadaki herkes doner
            // ve eleme bizim tarafta yapilir.
            if (titles.Count > 0)
                payload["person_titles"] = titles;

            // Kidem filtresi unvan metninden bagimsizdir (bkz. ApolloOptions.Seniorities
            // dokumantasyonu): "Bilgi İşlem Müdürü" ile "IT Manager" ayni kideme
            // sinifllanir. person_titles ile OR mantiginda calisir, aramayi daraltmaz.
            if (_options.Seniorities.Count > 0)
                payload["person_seniorities"] = _options.Seniorities.ToArray();

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.SearchEndpoint);
            request.Headers.Add("x-api-key", apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                System.Text.Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                    or System.Net.HttpStatusCode.Forbidden
                                    or System.Net.HttpStatusCode.PaymentRequired)
            {
                _logger.LogInformation(
                    "Apollo kişi araması kullanılamıyor ({Status}), yedek akışa geçilecek. {Body}",
                    (int)response.StatusCode, Truncate(body, 300));

                return BatchOutcome.NotAuthorized(
                    $"Apollo kişi araması bu anahtarla kullanılamıyor ({(int)response.StatusCode}).");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Apollo arama hatası {Status}: {Body}",
                    (int)response.StatusCode, Truncate(body, 300));

                return BatchOutcome.Failed($"Apollo arama API {(int)response.StatusCode}: {Truncate(body, 150)}");
            }

            return BatchOutcome.Success(ParsePeople(body, fetchSize));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Apollo kişi araması zaman aşımına uğradı: {Domain}", domain);
            return BatchOutcome.Failed("Apollo kişi araması zaman aşımına uğradı.");
        }
        catch (Exception ex)
        {
            // Zenginlestirme opsiyoneldir; hata olsa da ana akis durmaz.
            _logger.LogWarning(ex, "Apollo kişi araması başarısız: {Domain}", domain);
            return BatchOutcome.Failed(ex.Message);
        }
    }

    private readonly struct BatchOutcome
    {
        public List<PersonCandidate> People { get; init; }
        public string? Error { get; init; }
        public bool Unauthorized { get; init; }

        public static BatchOutcome Success(List<PersonCandidate> people) => new() { People = people };
        public static BatchOutcome Failed(string error) => new() { People = new(), Error = error };
        public static BatchOutcome NotAuthorized(string error) => new() { People = new(), Error = error, Unauthorized = true };
    }

    /// <summary>
    /// "api_search" ucu isim/e-posta acmaz: soyad kismen gizlidir ("Ak***y") ve
    /// email/telefon/linkedin alanlari hic gelmez, sadece "has_email" bayragi
    /// gelir. Gercek veriyi acmak icin cagiran taraf (HybridContactEnrichmentService)
    /// bu adayin <see cref="PersonCandidate.ApolloId"/>'siyle people/match'e
    /// ikinci bir istek atar.
    /// </summary>
    private static List<PersonCandidate> ParsePeople(string body, int maxResults)
    {
        var people = new List<PersonCandidate>();

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("people", out var array) || array.ValueKind != JsonValueKind.Array)
            return people;

        foreach (var person in array.EnumerateArray().Take(maxResults))
        {
            var id = Text(person, "id");
            var first = Text(person, "first_name");
            var lastObfuscated = Text(person, "last_name_obfuscated");
            var title = Text(person, "title");

            // Kimlik yoksa e-posta acma adiminda bu adayi sorgulayamayiz; anlami kalmaz.
            if (string.IsNullOrWhiteSpace(id))
                continue;

            // Gosterim amacli gecici isim: gercek soyad people/match sonrasi acilir
            // ve HybridContactEnrichmentService onu Contact.Name'e yazar.
            var displayName = string.Join(' ', new[] { first, lastObfuscated }.Where(p => !string.IsNullOrWhiteSpace(p)));
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = title ?? "(isim gizli)";

            people.Add(new PersonCandidate
            {
                Name = displayName.Trim(),
                FirstName = first,
                LastName = lastObfuscated,
                Title = title,
                ApolloId = id
            });
        }

        return people;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
