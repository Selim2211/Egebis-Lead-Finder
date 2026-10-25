using System.Text.Json;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class NvidiaAdapterTests
{
    private const string GeminiPayload = """
        {"system_instruction":{"parts":[{"text":"Sistem talimati"}]},
         "contents":[{"role":"user","parts":[{"text":"Firma metni"}]}],
         "generationConfig":{"temperature":0.1,"responseMimeType":"application/json",
           "responseSchema":{"type":"OBJECT","properties":{"ad":{"type":"STRING"}}}}}
        """;

    [Fact]
    public void Gemini_istegi_chat_istegine_cevrilir()
    {
        var json = OpenAiChatAdapter.ToChatRequest(GeminiPayload, "meta/llama-3.3-70b-instruct");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("meta/llama-3.3-70b-instruct", root.GetProperty("model").GetString());
        Assert.Equal(0.1, root.GetProperty("temperature").GetDouble());
        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        var system = messages[0].GetProperty("content").GetString()!;
        Assert.Contains("Sistem talimati", system);
        Assert.Contains("\"ad\"", system); // sema talimata eklenir
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Firma metni", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public void Chat_yaniti_gemini_zarfina_cevrilir_ve_kod_blogu_temizlenir()
    {
        const string chat = """
            {"choices":[{"message":{"role":"assistant","content":"```json\n{\"ad\":\"Test\"}\n```"}}],
             "usage":{"prompt_tokens":12,"completion_tokens":5}}
            """;

        var gemini = OpenAiChatAdapter.ToGeminiResponse(chat);
        using var doc = JsonDocument.Parse(gemini);
        var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content")
            .GetProperty("parts")[0].GetProperty("text").GetString();

        Assert.Equal("{\"ad\":\"Test\"}", text);
        Assert.Equal(12, doc.RootElement.GetProperty("usageMetadata").GetProperty("promptTokenCount").GetInt64());
    }

    [Theory]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("Işte sonuç: {\"a\":1} Umarım yardımcı olur.", "{\"a\":1}")]
    [InlineData("```\n{\"a\":1}\n```", "{\"a\":1}")]
    public void Json_ayiklama(string input, string expected) =>
        Assert.Equal(expected, OpenAiChatAdapter.ExtractJson(input));

    [Theory]
    [InlineData("openai/gpt-oss-20b", "reasoning_effort")]
    [InlineData("nvidia/nemotron-3.5-lightning-30b-a3b", "chat_template_kwargs")]
    [InlineData("deepseek-ai/deepseek-v4.1-flash", "chat_template_kwargs")]
    public void Dusunme_model_ailesine_gore_kapatilir(string model, string param)
    {
        using var on = JsonDocument.Parse(OpenAiChatAdapter.ToChatRequest(GeminiPayload, model));
        Assert.True(on.RootElement.TryGetProperty(param, out _));

        using var off = JsonDocument.Parse(OpenAiChatAdapter.ToChatRequest(GeminiPayload, model, thinkingOff: false));
        Assert.False(off.RootElement.TryGetProperty(param, out _));
    }

    [Fact]
    public void Json_modu_yalnizca_istenince_eklenir()
    {
        using var on = JsonDocument.Parse(OpenAiChatAdapter.ToChatRequest(GeminiPayload, "openai/gpt-oss-120b", jsonMode: true));
        Assert.Equal("json_object", on.RootElement.GetProperty("response_format").GetProperty("type").GetString());

        using var off = JsonDocument.Parse(OpenAiChatAdapter.ToChatRequest(GeminiPayload, "openai/gpt-oss-120b"));
        Assert.False(off.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public void Turkce_kurali_talimatin_sonunda_tekrarlanir()
    {
        var payload = GeminiPayload.Replace("Sistem talimati", "Tüm metin alanlarını Türkçe yaz.");
        using var doc = JsonDocument.Parse(OpenAiChatAdapter.ToChatRequest(payload, "openai/gpt-oss-120b"));
        var system = doc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.EndsWith("Alan adları şemadaki gibi kalsın.", system);
    }

    [Fact]
    public void Token_butcesi_varsa_kaynak_metin_kirpilir_sistem_korunur()
    {
        var longText = new string('a', 60_000);
        var payload = GeminiPayload.Replace("Firma metni", longText);
        using var doc = JsonDocument.Parse(OpenAiChatAdapter.ToChatRequest(payload, "openai/gpt-oss-120b", tokenBudget: 7000));
        var root = doc.RootElement;
        var maxTokens = root.GetProperty("max_tokens").GetInt32();
        var messages = root.GetProperty("messages");
        var system = messages[0].GetProperty("content").GetString()!;
        var user = messages[1].GetProperty("content").GetString()!;

        Assert.Contains("\"ad\"", system); // sema kirpilmaz
        Assert.Contains("kısaltıldı", user);
        var total = OpenAiChatAdapter.EstimateTokens(system) + OpenAiChatAdapter.EstimateTokens(user) + maxTokens;
        Assert.True(total <= 7100, $"toplam {total}");
    }

    [Fact]
    public void Think_blogu_cevaptan_atilir() =>
        Assert.Equal("{\"a\":1}", OpenAiChatAdapter.ExtractJson("<think>uzun düşünce</think>\n{\"a\":1}"));

    [Fact]
    public void Bos_yanit_aday_icermez()
    {
        var gemini = OpenAiChatAdapter.ToGeminiResponse("""{"choices":[]}""");
        using var doc = JsonDocument.Parse(gemini);
        Assert.False(doc.RootElement.TryGetProperty("candidates", out _));
    }
}
