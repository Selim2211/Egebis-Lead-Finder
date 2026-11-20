using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

/// <summary>Haber akisi Groq'ta; Gemini sorunlu olunca yonetici secimiyle Groq'a gecis.</summary>
public class AiFallbackTests
{
    private static FakeSettingsService Settings(params (string Key, string? Value)[] items) =>
        new(items.ToDictionary(i => i.Key, i => i.Value));

    [Fact]
    public async Task Varsayilan_gemini_ve_gecis_yoksa_ana_saglayici_gemini()
    {
        var s = Settings((SettingKeys.GeminiApiKey, "g"), (SettingKeys.GroqApiKey, "q"));
        Assert.Null(await AiProviders.ChatProviderAsync(s, default));
    }

    [Fact]
    public async Task Gecis_acikken_ve_groq_anahtari_varken_firma_analizi_groq_kullanir()
    {
        var s = Settings((SettingKeys.GroqApiKey, "q"), (SettingKeys.AiGeminiFallback, "1"));
        Assert.Equal(AiProviders.Groq, (await AiProviders.ChatProviderAsync(s, default))!.Id);
    }

    [Fact]
    public async Task Gecis_acik_ama_groq_anahtari_yoksa_gemini_kalir()
    {
        var s = Settings((SettingKeys.AiGeminiFallback, "1"));
        Assert.Null(await AiProviders.ChatProviderAsync(s, default));
    }

    [Fact]
    public async Task Gecis_kalkinca_gemini_geri_gelir()
    {
        var s = Settings((SettingKeys.GroqApiKey, "q"), (SettingKeys.AiGeminiFallback, "1"));
        await s.SetManyAsync(new Dictionary<string, string?> { [SettingKeys.AiGeminiFallback] = null });
        Assert.Null(await AiProviders.ChatProviderAsync(s, default));
    }

    [Fact]
    public async Task Haber_akisi_varsayilan_olarak_groq_kullanir_gemini_anahtari_olsa_bile()
    {
        var s = Settings((SettingKeys.GeminiApiKey, "g"), (SettingKeys.GroqApiKey, "q"));
        Assert.Equal(AiProviders.Groq, (await AiProviders.NewsProviderAsync(s, default))!.Id);
    }

    [Fact]
    public async Task Haber_akisi_groq_anahtari_yoksa_ya_da_ana_saglayici_secilirse_ana_saglayiciya_duser()
    {
        Assert.Null(await AiProviders.NewsProviderAsync(Settings((SettingKeys.GeminiApiKey, "g")), default));
        Assert.Null(await AiProviders.NewsProviderAsync(Settings((SettingKeys.GroqApiKey, "q"), (SettingKeys.NewsAiSource, "main")), default));
    }

    [Fact]
    public async Task Zorlanan_saglayici_yalniz_kapsam_icinde_gecerlidir()
    {
        var s = Settings((SettingKeys.GeminiApiKey, "g"), (SettingKeys.GroqApiKey, "q"));
        using (AiProviders.Force(AiProviders.GroqProvider))
            Assert.Equal(AiProviders.Groq, (await AiProviders.ChatProviderAsync(s, default))!.Id);
        Assert.Null(await AiProviders.ChatProviderAsync(s, default));
    }
}
