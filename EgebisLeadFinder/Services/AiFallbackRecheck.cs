using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Gemini sorunu yuzunden yonetici Groq'a gectiyse (AiGeminiFallback = "1"), her uygulama acilisinda Gemini
/// yoklanir; anahtar/lisans duzelmisse bayrak kalkar ve yapay zeka kendiliginden Gemini'ye doner.
/// </summary>
public class AiFallbackRecheck : BackgroundService
{
    private readonly ISettingsService _settings;
    private readonly ApiHealthService _health;
    private readonly ILogger<AiFallbackRecheck> _logger;

    public AiFallbackRecheck(ISettingsService settings, ApiHealthService health, ILogger<AiFallbackRecheck> logger)
    {
        _settings = settings;
        _health = health;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), ct); // acilis yuku bitsin
            if (await _settings.GetAsync(SettingKeys.AiGeminiFallback, ct) != "1") return;

            var key = await _settings.GetAsync(SettingKeys.GeminiApiKey, ct);
            if (string.IsNullOrWhiteSpace(key))
            {
                _logger.LogInformation("Gemini anahtarı yok: yapay zekâ Groq'ta kalıyor.");
                return;
            }

            var error = await _health.CheckAsync("gemini", key, ct);
            if (error is null)
            {
                await _settings.SetManyAsync(new Dictionary<string, string?> { [SettingKeys.AiGeminiFallback] = null }, ct);
                _logger.LogInformation("Gemini yeniden çalışıyor: yapay zekâ otomatik olarak Gemini'ye döndü.");
            }
            else
            {
                _logger.LogInformation("Gemini hâlâ sorunlu ({Error}): yapay zekâ Groq'ta kalıyor.", error);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini yeniden kontrolü başarısız.");
        }
    }
}
