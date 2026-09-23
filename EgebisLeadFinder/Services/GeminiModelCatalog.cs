using System.Text.Json;
using System.Text.RegularExpressions;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services;

/// <summary>Ayarlar ekranindaki model listesinde bir satir.</summary>
public record GeminiModelInfo(string Id, string DisplayName, string? Description, int? InputTokenLimit);

public interface IGeminiModelCatalog
{
    /// <summary>
    /// Anahtarin erisebildigi, metin uretebilen Gemini modelleri (Google'dan canli).
    /// Anahtar yoksa veya istek basarisizsa Error dolu, liste bos doner.
    /// </summary>
    Task<(IReadOnlyList<GeminiModelInfo> Models, string? Error)> ListAsync(CancellationToken ct = default);

    /// <summary>Kullanilacak model: Ayarlar'daki secim, yoksa appsettings varsayilani.</summary>
    Task<string> GetCurrentModelAsync(CancellationToken ct = default);
}

/// <summary>
/// Model listesi elle yazilmaz; Gemini API'nin models ucundan cekilir. Boylece hem
/// yeni cikan modeller gorunur hem de hesabin erisemedigi bir model secilemez.
/// </summary>
public partial class GeminiModelCatalog : IGeminiModelCatalog
{
    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly ISettingsService _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GeminiModelCatalog> _logger;

    public GeminiModelCatalog(HttpClient http, IOptions<AiOptions> options, ISettingsService settings,
        IMemoryCache cache, ILogger<GeminiModelCatalog> logger)
    {
        _http = http;
        _options = options.Value;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.\\-]{1,80}$")]
    private static partial Regex ModelIdRegex();

    /// <summary>URL'ye eklenecegi icin yalnizca sade model adlarina izin verilir.</summary>
    public static bool IsValidModelId(string? id) => id is not null && ModelIdRegex().IsMatch(id);

    public async Task<string> GetCurrentModelAsync(CancellationToken ct = default)
    {
        var stored = (await _settings.GetAsync(SettingKeys.GeminiModel, ct))?.Trim();
        return IsValidModelId(stored) ? stored! : _options.GeminiModel;
    }

    public async Task<(IReadOnlyList<GeminiModelInfo> Models, string? Error)> ListAsync(CancellationToken ct = default)
    {
        var apiKey = await _settings.GetAsync(SettingKeys.GeminiApiKey, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            return (Array.Empty<GeminiModelInfo>(), "Model listesi için önce Gemini API anahtarını kaydedin.");

        // Anahtar degisince liste de yenilensin diye onbellek anahtarina parmak izi eklenir.
        var cacheKey = "gemini-models:" + apiKey.GetHashCode();
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<GeminiModelInfo>? cached) && cached is not null)
            return (cached, null);

        try
        {
            var models = new List<GeminiModelInfo>();
            string? pageToken = null;

            do
            {
                var url = $"{_options.GeminiEndpoint}?pageSize=200" +
                          (pageToken is null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-goog-api-key", apiKey);

                using var response = await _http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Gemini model listesi alınamadı: {Status}", (int)response.StatusCode);
                    return (Array.Empty<GeminiModelInfo>(), $"Model listesi alınamadı (Gemini {(int)response.StatusCode}).");
                }

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("models", out var arr))
                    models.AddRange(arr.EnumerateArray().Select(Parse).Where(m => m is not null)!);

                pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var t) ? t.GetString() : null;
            } while (!string.IsNullOrEmpty(pageToken));

            var result = models
                .DistinctBy(m => m.Id)
                .OrderBy(m => m.Id.Contains("preview") || m.Id.Contains("exp"))   // kararli surumler once
                .ThenByDescending(m => m.Id, StringComparer.Ordinal)
                .ToList();

            _cache.Set(cacheKey, (IReadOnlyList<GeminiModelInfo>)result, TimeSpan.FromHours(6));
            return (result, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Gemini model listesi alınamadı.");
            return (Array.Empty<GeminiModelInfo>(), "Model listesi alınamadı; bağlantıyı kontrol edin.");
        }
    }

    /// <summary>
    /// Yalnizca metin uretebilen (generateContent) Gemini modelleri; embedding, gorsel/ses
    /// uretimi ve canli (live) modeller firma analizi icin uygun degil.
    /// </summary>
    public static GeminiModelInfo? Parse(JsonElement m)
    {
        var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (name is null || !name.StartsWith("models/gemini", StringComparison.Ordinal)) return null;

        var methods = m.TryGetProperty("supportedGenerationMethods", out var sm)
            ? sm.EnumerateArray().Select(x => x.GetString()).ToList()
            : new List<string?>();
        if (!methods.Contains("generateContent")) return null;

        var id = name["models/".Length..];
        if (!IsValidModelId(id)) return null;
        string[] specialPurpose = { "embedding", "image", "tts", "live", "audio", "transcribe",
            "robotics", "computer-use", "customtools" };
        if (specialPurpose.Any(id.Contains)) return null;

        return new GeminiModelInfo(
            id,
            m.TryGetProperty("displayName", out var d) ? d.GetString() ?? id : id,
            m.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            m.TryGetProperty("inputTokenLimit", out var lim) && lim.TryGetInt32(out var l) ? l : null);
    }
}
