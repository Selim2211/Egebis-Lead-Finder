using System.Text.Json;

namespace EgebisLeadFinder.Services;

public interface IExchangeRateService
{
    /// <summary>Guncel 1 USD = ? TRY. Kaynağa ulaşılamazsa null döner (çağıran taraf yedek değer kullanır).</summary>
    Task<decimal?> GetUsdTryRateAsync(CancellationToken ct = default);
}

/// <summary>
/// Gemini kullanım maliyetini TL'ye çevirmek için güncel USD/TRY kurunu, anahtar
/// gerektirmeyen ücretsiz bir kur API'sinden okur. Kullanıcının kuru elle takip
/// etmesine gerek kalmaz ("kura göre hesapla sen" — bkz. Ayarlar ekranı).
/// </summary>
public class ExchangeRateService : IExchangeRateService
{
    private readonly HttpClient _http;
    private readonly ILogger<ExchangeRateService> _logger;

    public ExchangeRateService(HttpClient http, ILogger<ExchangeRateService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<decimal?> GetUsdTryRateAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var response = await _http.GetAsync("https://open.er-api.com/v6/latest/USD", cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

            if (doc.RootElement.TryGetProperty("rates", out var rates) &&
                rates.TryGetProperty("TRY", out var tryRate) &&
                tryRate.TryGetDecimal(out var value))
                return value;

            return null;
        }
        catch (Exception ex)
        {
            // Kur bilgisi kritik degil; alinamazsa cagiran taraf sabit yedek deger kullanir.
            _logger.LogWarning(ex, "USD/TRY kuru alınamadı.");
            return null;
        }
    }
}
