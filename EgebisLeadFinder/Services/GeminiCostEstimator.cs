using EgebisLeadFinder.Configuration;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Cagri basina TAHMINI Gemini maliyeti. Uygulama gercek token sayisini tutmaz;
/// AiOptions'taki sabit fiyatlar ve ortalama karakter/token varsayimlariyla
/// kaba bir tahmin uretir. Amac kesin fatura degil, "yaklasik ne kadar harciyoruz"
/// sorusuna Ayarlar ekraninda cevap vermek.
/// </summary>
public static class GeminiCostEstimator
{
    private const decimal CharsPerToken = 4m;

    public static decimal EstimateCostPerCallTry(AiOptions options, decimal usdTryRate)
    {
        var inputTokens = options.EstimatedInputCharsPerCall / CharsPerToken;
        var outputTokens = (decimal)options.EstimatedOutputTokensPerCall;

        var costUsd =
            inputTokens / 1_000_000m * options.InputPricePerMillionTokensUsd +
            outputTokens / 1_000_000m * options.OutputPricePerMillionTokensUsd;

        return Math.Round(costUsd * usdTryRate, 4);
    }
}
