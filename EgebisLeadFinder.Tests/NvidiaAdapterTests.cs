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
        var json = NvidiaChatAdapter.ToChatRequest(GeminiPayload, "meta/llama-3.3-70b-instruct");
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

        var gemini = NvidiaChatAdapter.ToGeminiResponse(chat);
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
        Assert.Equal(expected, NvidiaChatAdapter.ExtractJson(input));

    [Fact]
    public void Bos_yanit_aday_icermez()
    {
        var gemini = NvidiaChatAdapter.ToGeminiResponse("""{"choices":[]}""");
        using var doc = JsonDocument.Parse(gemini);
        Assert.False(doc.RootElement.TryGetProperty("candidates", out _));
    }
}
