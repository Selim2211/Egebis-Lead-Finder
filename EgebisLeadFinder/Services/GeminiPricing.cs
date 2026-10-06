using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>Gemini API (ucretli katman) metin fiyatlari, 1 milyon token basina USD. Kaynak: ai.google.dev/gemini-api/docs/pricing.</summary>
public static class GeminiPricing
{
    public const decimal DefaultUsdTryRate = 50m;

    // 3.6 / 3.7 / 3.8 Flash: tanitim fiyati 31.12.2026'ya kadar; 01.01.2027'den itibaren iki kati.
    private static readonly DateOnly IntroPriceEnds = new(2026, 12, 31);

    public static decimal ParseRate(string? raw) =>
        decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var r) && r > 0
            ? r : DefaultUsdTryRate;

    /// <summary>Modelin o gunku girdi/cikti fiyati. Tanimsiz modelde ayarlardaki varsayilan fiyatlar kullanilir.</summary>
    public static (decimal Input, decimal Output) PerMillion(string? model, DateOnly date, AiOptions fallback)
    {
        var m = (model ?? string.Empty).ToLowerInvariant();

        if (m.Contains("3.5-flash-lite")) return (0.30m, 2.50m);
        if (m.Contains("3.1-flash-lite")) return (0.25m, 1.50m);
        if (m.Contains("2.5-flash-lite")) return (0.10m, 0.40m);
        if (m.Contains("3.5-flash")) return (1.50m, 9.00m);
        if (m.Contains("3.8-flash") || m.Contains("3.7-flash") || m.Contains("3.6-flash"))
            return date <= IntroPriceEnds ? (0.75m, 3.75m) : (1.50m, 7.50m);
        if (m.Contains("2.5-flash")) return (0.30m, 2.50m);
        if (m.Contains("3.1-pro")) return (2.00m, 12.00m);
        if (m.Contains("2.5-pro")) return (1.25m, 10.00m);

        return (fallback.InputPricePerMillionTokensUsd, fallback.OutputPricePerMillionTokensUsd);
    }

    public static decimal CostUsd(string? model, DateOnly date, long inputTokens, long outputTokens, AiOptions fallback)
    {
        var (input, output) = PerMillion(model, date, fallback);
        return inputTokens / 1_000_000m * input + outputTokens / 1_000_000m * output;
    }
}

public record GeminiCost(decimal Try, decimal Usd, int MeasuredCalls, int EstimatedCalls, long InputTokens, long OutputTokens);

/// <summary>
/// Gemini maliyeti: gercek token sayilarindan (kayitli cagrilar) hesaplanir. Token kaydi olmayan eski cagrilar
/// (bu guncellemeden onceki) ortalama token varsayimiyla tahmin edilir ve ayri sayilir.
/// </summary>
public static class GeminiCostCalculator
{
    public static GeminiCost Compute(IEnumerable<GeminiTokenDaily> rows, int totalCalls, decimal usdTryRate, AiOptions options,
        string? currentModel, DateOnly today)
    {
        decimal usd = 0;
        var measured = 0;
        long input = 0, output = 0;
        foreach (var row in rows)
        {
            usd += GeminiPricing.CostUsd(row.Model, row.Date, row.InputTokens, row.OutputTokens, options);
            measured += row.Calls;
            input += row.InputTokens;
            output += row.OutputTokens;
        }

        var missing = Math.Max(0, totalCalls - measured);
        if (missing > 0)
        {
            var avgInput = options.EstimatedInputTokensPerCall;
            var avgOutput = options.EstimatedOutputTokensPerCall;
            usd += missing * GeminiPricing.CostUsd(currentModel ?? options.GeminiModel, today, avgInput, avgOutput, options);
        }

        var rate = usdTryRate > 0 ? usdTryRate : GeminiPricing.DefaultUsdTryRate;
        return new GeminiCost(Math.Round(usd * rate, 2), usd, measured, missing, input, output);
    }
}
