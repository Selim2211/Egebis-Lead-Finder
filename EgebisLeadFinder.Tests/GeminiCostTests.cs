using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class GeminiCostTests
{
    private static readonly AiOptions Options = new();

    [Fact]
    public void Flash_3_6_fiyati_yil_sonuna_kadar_tanitim_fiyatidir_sonra_iki_kati()
    {
        Assert.Equal((0.75m, 3.75m), GeminiPricing.PerMillion("gemini-3.6-flash", new DateOnly(2026, 10, 6), Options));
        Assert.Equal((1.50m, 7.50m), GeminiPricing.PerMillion("gemini-3.6-flash", new DateOnly(2027, 1, 1), Options));
    }

    [Fact]
    public void Flash_lite_ve_pro_modelleri_kendi_fiyatiyla_hesaplanir()
    {
        var day = new DateOnly(2026, 10, 6);
        Assert.Equal((0.30m, 2.50m), GeminiPricing.PerMillion("gemini-3.5-flash-lite", day, Options));
        Assert.Equal((1.50m, 9.00m), GeminiPricing.PerMillion("gemini-3.5-flash", day, Options));
        Assert.Equal((2.00m, 12.00m), GeminiPricing.PerMillion("gemini-3.1-pro-preview", day, Options));
        Assert.Equal((Options.InputPricePerMillionTokensUsd, Options.OutputPricePerMillionTokensUsd),
            GeminiPricing.PerMillion("bilinmeyen-model", day, Options));
    }

    [Fact]
    public void Maliyet_gercek_tokenlerden_ve_50_TL_kurundan_hesaplanir()
    {
        var day = new DateOnly(2026, 10, 6);
        var rows = new[]
        {
            new GeminiTokenDaily { Date = day, Model = "gemini-3.6-flash", Calls = 2, InputTokens = 2_000_000, OutputTokens = 1_000_000 }
        };

        var cost = GeminiCostCalculator.Compute(rows, totalCalls: 2, usdTryRate: 50m, Options, "gemini-3.6-flash", day);

        // 2M x 0.75 + 1M x 3.75 = 5.25 USD -> 262.50 TL
        Assert.Equal(5.25m, cost.Usd);
        Assert.Equal(262.50m, cost.Try);
        Assert.Equal(2, cost.MeasuredCalls);
        Assert.Equal(0, cost.EstimatedCalls);
    }

    [Fact]
    public void Token_kaydi_olmayan_eski_cagrilar_tahminle_eklenir_ve_ayri_sayilir()
    {
        var day = new DateOnly(2026, 10, 6);
        var rows = new[] { new GeminiTokenDaily { Date = day, Model = "gemini-3.6-flash", Calls = 1, InputTokens = 1_000_000, OutputTokens = 0 } };

        var cost = GeminiCostCalculator.Compute(rows, totalCalls: 11, usdTryRate: 50m, Options, "gemini-3.6-flash", day);

        Assert.Equal(1, cost.MeasuredCalls);
        Assert.Equal(10, cost.EstimatedCalls);
        Assert.True(cost.Usd > 0.75m);
    }

    [Fact]
    public void Gecersiz_kur_varsayilan_50_olur()
    {
        Assert.Equal(50m, GeminiPricing.ParseRate(null));
        Assert.Equal(50m, GeminiPricing.ParseRate("0"));
        Assert.Equal(48.5m, GeminiPricing.ParseRate("48.5"));
    }
}
