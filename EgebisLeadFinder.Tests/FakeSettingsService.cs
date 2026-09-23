using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

/// <summary>Testlerde veritabanina gitmeden ayar dondurur.</summary>
public class FakeSettingsService : ISettingsService
{
    private readonly Dictionary<string, string?> _values;
    private readonly List<string>? _titleKeywords;

    public FakeSettingsService(
        Dictionary<string, string?>? values = null,
        List<string>? titleKeywords = null)
    {
        _values = values ?? new Dictionary<string, string?>();
        _titleKeywords = titleKeywords;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

    public Task<Dictionary<string, string?>> GetStoredAsync(CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, string?>(_values));

    public Task SetManyAsync(IDictionary<string, string?> values, CancellationToken ct = default)
    {
        foreach (var (k, v) in values) _values[k] = v;
        return Task.CompletedTask;
    }

    public Task<List<string>> GetTitleKeywordsAsync(CancellationToken ct = default) =>
        Task.FromResult(_titleKeywords ?? SettingsService.DefaultTitleKeywords.ToList());

    /// <summary>Gemini testleri icin: anahtari hazir kurar.</summary>
    public static FakeSettingsService WithGeminiKeys(string apiKey) =>
        new(new Dictionary<string, string?>
        {
            [SettingKeys.GeminiApiKey] = apiKey
        });
}
