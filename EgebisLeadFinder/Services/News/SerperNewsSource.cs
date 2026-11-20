using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EgebisLeadFinder.Configuration;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services.News;

/// <summary>Yedek kaynak: Serper /news ucu (sorgu başına 1 kredi). Yalnızca RSS hata verince kullanılır.</summary>
public class SerperNewsSource : INewsSource
{
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly IApiUsageTracker _usage;
    private readonly NewsOptions _options;

    public SerperNewsSource(HttpClient http, ISettingsService settings, IApiUsageTracker usage, IOptions<NewsOptions> options)
    {
        _http = http;
        _settings = settings;
        _usage = usage;
        _options = options.Value;
    }

    public string Name => "Serper Haber";
    public bool IsFallback => true;

    public async Task<List<NewsHit>> SearchAsync(string query, CancellationToken ct = default)
    {
        var key = await _settings.GetAsync(Models.SettingKeys.SerperApiKey, ct);
        if (string.IsNullOrWhiteSpace(key)) throw new MissingApiKeyException("Serper");

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.SerperNewsEndpoint);
        request.Headers.Add("X-API-KEY", key);
        request.Content = JsonContent.Create(new { q = query, gl = "tr", hl = "tr", num = 10 });

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        await _usage.IncrementAsync(SerperSearchService.UsageProvider, ct);

        var payload = await response.Content.ReadFromJsonAsync<Response>(cancellationToken: ct);
        return (payload?.News ?? new List<Item>())
            .Where(n => !string.IsNullOrWhiteSpace(n.Title) && !string.IsNullOrWhiteSpace(n.Link))
            // Serper tarihi "2 hours ago" gibi görelidir; güvenilir ayrıştırılamadığı için toplanma zamanı kullanılır.
            .Select(n => new NewsHit(n.Title!.Trim(), n.Link!.Trim(), n.Source, null, n.Snippet, Models.NewsRegions.Tr))
            .ToList();
    }

    private class Response
    {
        [JsonPropertyName("news")] public List<Item>? News { get; set; }
    }

    private class Item
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("link")] public string? Link { get; set; }
        [JsonPropertyName("snippet")] public string? Snippet { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
    }
}
