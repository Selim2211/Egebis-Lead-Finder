using System.Text.Json;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class ChatModelPoolTests
{
    [Fact]
    public void Virgulle_yazilan_liste_sirali_ve_tekil_okunur()
    {
        var list = ChatModelPool.Parse(" openai/gpt-oss-120b , openai/gpt-oss-20b;openai/gpt-oss-120b\nqwen/x ");
        Assert.Equal(new[] { "openai/gpt-oss-120b", "openai/gpt-oss-20b", "qwen/x" }, list);
    }

    [Fact]
    public void Dolu_model_atlanir_sure_bitince_geri_gelir()
    {
        var provider = "test-" + Guid.NewGuid();
        var models = new[] { "a", "b" };
        var now = DateTimeOffset.UtcNow;

        ChatModelPool.Block(provider, "a", TimeSpan.FromMinutes(5), now);
        Assert.Equal("b", ChatModelPool.Next(provider, models, new List<string>(), now));
        Assert.Null(ChatModelPool.Next(provider, models, new List<string> { "b" }, now));
        Assert.Equal("a", ChatModelPool.Next(provider, models, new List<string>(), now.AddMinutes(6)));
    }

    [Theory]
    [InlineData("Rate limit reached for model `x` on tokens per day (TPD): Limit 200000", true)]
    [InlineData("Rate limit reached on requests per day (RPD)", true)]
    [InlineData("Rate limit reached on tokens per minute (TPM): Limit 8000", false)]
    public void Gunluk_sinir_tanınır(string body, bool daily) =>
        Assert.Equal(daily, ChatModelPool.IsDailyLimit(body));

    [Fact]
    public void Bekleme_suresi_hata_metninden_okunur()
    {
        Assert.Equal(TimeSpan.FromSeconds(7 * 60 + 12.5), ChatModelPool.ParseTryAgain("Please try again in 7m12.5s. Need more tokens?"));
        Assert.Equal(TimeSpan.FromMilliseconds(950), ChatModelPool.ParseTryAgain("Please try again in 950ms."));
        Assert.Null(ChatModelPool.ParseTryAgain("no hint here"));
    }

    [Fact]
    public void Gunluk_sinirda_en_az_yarim_saat_dakikalikta_kisa_bekletilir()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), ChatModelPool.BlockDuration(429, "tokens per day (TPD)", null));
        Assert.Equal(TimeSpan.FromSeconds(20), ChatModelPool.BlockDuration(429, "tokens per minute", TimeSpan.FromSeconds(20)));
        Assert.Equal(TimeSpan.FromHours(24), ChatModelPool.BlockDuration(400, "The model `x` has been decommissioned", null));
    }

    [Fact]
    public void Kullanilan_model_analiz_jsonuna_eklenir()
    {
        const string chat = """{"choices":[{"message":{"content":"{\"industry\":\"Tesisat\"}"}}]}""";
        using var doc = JsonDocument.Parse(OpenAiChatAdapter.ToGeminiResponse(chat, "openai/gpt-oss-20b"));
        var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0]
            .GetProperty("text").GetString()!;
        using var inner = JsonDocument.Parse(text);
        Assert.Equal("openai/gpt-oss-20b", inner.RootElement.GetProperty("aiModel").GetString());
        Assert.Equal("Tesisat", inner.RootElement.GetProperty("industry").GetString());
    }
}
