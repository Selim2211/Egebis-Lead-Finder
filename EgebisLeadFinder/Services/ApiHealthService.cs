using System.Net.Sockets;
using System.Text.Json;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Caching.Memory;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Acilis uyarisi icin canli erisim kontrolu: kullanilan her API'ye kredi harcamayan kucuk bir
/// istek atilir (hesap / model listesi / auth health). Ulasilamazsa (ag, DNS, zaman asimi),
/// anahtar reddedilirse (401/403), kredi bittiyse (402, bakiye 0) ya da servis hata verirse (5xx)
/// nedeni okunur bir cumleyle doner. Sonuc 5 dk onbellekte tutulur.
/// </summary>
public class ApiHealthService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private readonly IHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ApiHealthService> _logger;

    public ApiHealthService(IHttpClientFactory httpFactory, IMemoryCache cache, ILogger<ApiHealthService> logger)
    {
        _httpFactory = httpFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Servis anahtari ("serper", "gemini", "groq", "nvidia", "apollo") icin hata metni; sorun yoksa null.</summary>
    public async Task<string?> CheckAsync(string service, string apiKey, CancellationToken ct = default)
    {
        var cacheKey = "health:" + service + ":" + apiKey.GetHashCode();
        if (_cache.TryGetValue(cacheKey, out (bool Done, string? Error) hit) && hit.Done) return hit.Error;

        var request = BuildRequest(service, apiKey);
        if (request is null) return null;

        string? error;
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            using (request)
            using (var response = await http.SendAsync(request, ct))
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                error = Classify(service, (int)response.StatusCode, body);
            }
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            error = "Sunucudan ulaşılamadı: 8 saniyede yanıt gelmedi (ağ, güvenlik duvarı ya da servis yavaş)";
        }
        catch (HttpRequestException ex)
        {
            error = "Sunucudan ulaşılamadı: " + NetworkReason(ex);
        }

        if (error is not null) _logger.LogWarning("API erişim kontrolü başarısız ({Service}): {Error}", service, error);
        _cache.Set(cacheKey, (true, error), CacheFor);
        return error;
    }

    private static HttpRequestMessage? BuildRequest(string service, string key)
    {
        HttpRequestMessage r;
        switch (service)
        {
            case "serper":
                r = new HttpRequestMessage(HttpMethod.Get, "https://google.serper.dev/account");
                r.Headers.Add("X-API-KEY", key);
                return r;
            case "apollo":
                r = new HttpRequestMessage(HttpMethod.Get, "https://api.apollo.io/api/v1/auth/health");
                r.Headers.Add("x-api-key", key);
                return r;
            case "gemini":
                r = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1");
                r.Headers.Add("x-goog-api-key", key);
                return r;
            case AiProviders.Groq:
                r = new HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/openai/v1/models");
                r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                return r;
            case AiProviders.Nvidia:
                r = new HttpRequestMessage(HttpMethod.Get, "https://integrate.api.nvidia.com/v1/models");
                r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                return r;
            default:
                return null;
        }
    }

    /// <summary>Yanit kodu/govdesinden hata metni. 404/405 gibi "uc nokta degisti" yanitlari erisim sorunu sayilmaz.</summary>
    public static string? Classify(string service, int status, string? body)
    {
        var lower = (body ?? string.Empty).ToLowerInvariant();

        if (status is >= 200 and < 300)
        {
            if (service == "apollo" && TryBool(body, "is_logged_in") == false)
                return "API anahtarı geçersiz: Apollo anahtarı tanımadı (is_logged_in = false)";
            if (service == "serper" && TryNumber(body, "balance") is <= 0)
                return "Kredi bitti: Serper bakiyesi 0";
            return null;
        }

        // Gemini gecersiz anahtara 400 doner.
        if (status == 400 && (lower.Contains("api key not valid") || lower.Contains("api_key_invalid")))
            return ApiErrorText.Describe(401, body).Replace("(HTTP 401)", "(HTTP 400)");
        if (status is 401 or 402 or 403 or 429) return ApiErrorText.Describe(status, body);
        if (status >= 500) return $"Servis şu an hata veriyor (HTTP {status}); sağlayıcı tarafında arıza olabilir";
        return null;
    }

    private static string NetworkReason(HttpRequestException ex)
    {
        var inner = ex.InnerException;
        if (inner is SocketException se)
            return se.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData => "alan adı çözülemedi (DNS sorunu)",
                SocketError.ConnectionRefused => "bağlantı reddedildi",
                SocketError.TimedOut => "bağlantı zaman aşımı",
                SocketError.NetworkUnreachable or SocketError.HostUnreachable => "ağ erişimi yok (internet/proxy/güvenlik duvarı)",
                _ => se.Message
            };
        if (inner is System.Security.Authentication.AuthenticationException)
            return "SSL/TLS doğrulaması başarısız (proxy ya da sertifika sorunu)";
        return ex.Message;
    }

    private static bool? TryBool(string? body, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(body ?? "");
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? v.GetBoolean() : null;
        }
        catch (JsonException) { return null; }
    }

    private static double? TryNumber(string? body, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(body ?? "");
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDouble() : null;
        }
        catch (JsonException) { return null; }
    }
}
