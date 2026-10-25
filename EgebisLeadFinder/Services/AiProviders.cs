using System.Text.Json;
using System.Text.Json.Nodes;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Yapay zeka saglayicisi secimi. Varsayilan Gemini; Ayarlar'dan NVIDIA (OpenAI uyumlu
/// chat completions) secilebilir. Cagri noktalari Gemini bicimli istek uretmeye devam eder;
/// NVIDIA icin istek/yanit <see cref="NvidiaChatAdapter"/> ile tasima katmaninda cevrilir.
/// </summary>
public static class AiProviders
{
    public const string Gemini = "gemini";
    public const string Nvidia = "nvidia";
    public const string NvidiaEndpoint = "https://integrate.api.nvidia.com/v1/chat/completions";
    public const string NvidiaDefaultModel = "nvidia/nemotron-3.5-lightning-30b-a3b";

    /// <summary>API kullanim sayacinda NVIDIA cagrilari bu adla tutulur.</summary>
    public const string NvidiaUsageProvider = "Nvidia";

    public static async Task<bool> IsNvidiaAsync(ISettingsService settings, CancellationToken ct) =>
        string.Equals((await settings.GetAsync(SettingKeys.AiProvider, ct))?.Trim(), Nvidia,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Secili saglayicinin API anahtari (yalnizca Ayarlar ekranindan).</summary>
    public static async Task<string?> ActiveKeyAsync(ISettingsService settings, CancellationToken ct) =>
        await IsNvidiaAsync(settings, ct)
            ? await settings.GetAsync(SettingKeys.NvidiaApiKey, ct)
            : await settings.GetAsync(SettingKeys.GeminiApiKey, ct);

    public static async Task<string> NvidiaModelAsync(ISettingsService settings, CancellationToken ct)
    {
        var stored = (await settings.GetAsync(SettingKeys.NvidiaModel, ct))?.Trim();
        return string.IsNullOrWhiteSpace(stored) ? NvidiaDefaultModel : stored;
    }
}

/// <summary>Gemini bicimli istegi OpenAI chat istegine, chat yanitini Gemini zarfina cevirir.</summary>
public static class NvidiaChatAdapter
{
    // Dusunme kapali olsa bile bazi modeller kisa bir akil yurutme yazar; JSON yarim kalmasin.
    private const int MaxTokens = 8192;

    /// <summary>
    /// Yeni NVIDIA modellerinin cogu cevaptan once uzun "dusunur" (reasoning); firma basina
    /// 45+ sn surer. Model ailesine gore dusunmeyi kapatan / en aza indiren parametreler eklenir.
    /// Model bir parametreyi tanimazsa (HTTP 400) cagiran taraf parametresiz yeniden dener.
    /// </summary>
    public static bool AddThinkingOff(JsonObject body, string model)
    {
        var m = model.ToLowerInvariant();
        if (m.Contains("gpt-oss"))
        {
            body["reasoning_effort"] = "low";
            return true;
        }
        if (m.Contains("nemotron") || m.Contains("qwen"))
        {
            body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
            return true;
        }
        if (m.Contains("deepseek") || m.Contains("kimi"))
        {
            body["chat_template_kwargs"] = new JsonObject { ["thinking"] = false };
            return true;
        }
        return false;
    }

    public static string ToChatRequest(string geminiPayloadJson, string model, bool thinkingOff = true)
    {
        var src = JsonNode.Parse(geminiPayloadJson)!.AsObject();
        var gen = src["generationConfig"] as JsonObject;
        var wantsJson = (gen?["responseMimeType"] as JsonValue)?.GetValue<string>() == "application/json";

        var system = string.Join("\n", Texts(src["system_instruction"]));
        if (wantsJson)
        {
            system += "\n\nYANIT BİÇİMİ: Yalnızca geçerli bir JSON nesnesi döndür. Markdown, kod bloğu (```) " +
                      "veya açıklama ekleme; JSON dışında hiçbir karakter yazma.";
            if (gen?["responseSchema"] is { } schema)
                system += "\nJSON şeması (alan adları ve tipleri birebir bu şemaya uymalı):\n" + schema.ToJsonString();
        }

        var messages = new JsonArray();
        if (!string.IsNullOrWhiteSpace(system))
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = system });

        if (src["contents"] is JsonArray contents)
        {
            foreach (var c in contents)
            {
                var role = (c?["role"] as JsonValue)?.GetValue<string>();
                messages.Add(new JsonObject
                {
                    ["role"] = role == "model" ? "assistant" : "user",
                    ["content"] = string.Join("\n", Texts(c))
                });
            }
        }

        var temperature = gen?["temperature"] is JsonValue t && t.TryGetValue<double>(out var d) ? d : 0.2;
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = temperature,
            ["max_tokens"] = MaxTokens,
            ["stream"] = false
        };
        if (thinkingOff) AddThinkingOff(body, model);
        return body.ToJsonString();
    }

    /// <summary>OpenAI chat yanitini, mevcut ExtractText'in bekledigi Gemini zarfina cevirir.</summary>
    public static string ToGeminiResponse(string chatResponseJson)
    {
        using var doc = JsonDocument.Parse(chatResponseJson);
        var root = doc.RootElement;

        string? text = null;
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            text = ExtractJson(content.GetString() ?? string.Empty);
        }

        long Read(string name) =>
            root.TryGetProperty("usage", out var u) && u.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;

        var result = new JsonObject
        {
            ["usageMetadata"] = new JsonObject
            {
                ["promptTokenCount"] = Read("prompt_tokens"),
                ["candidatesTokenCount"] = Read("completion_tokens")
            }
        };
        if (text is not null)
        {
            result["candidates"] = new JsonArray(new JsonObject
            {
                ["content"] = new JsonObject
                {
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = text })
                }
            });
        }
        return result.ToJsonString();
    }

    /// <summary>Model ```json ... ``` ile sarsa ya da onune/arkasina metin eklese bile JSON'u ayiklar.</summary>
    public static string ExtractJson(string text)
    {
        var t = text.Trim();
        // Bazi modeller dusunmeyi <think>...</think> olarak cevabin basina yazar.
        var thinkEnd = t.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (thinkEnd >= 0) t = t[(thinkEnd + "</think>".Length)..].Trim();
        if (t.StartsWith("```", StringComparison.Ordinal))
        {
            var nl = t.IndexOf('\n');
            if (nl >= 0) t = t[(nl + 1)..];
            var end = t.LastIndexOf("```", StringComparison.Ordinal);
            if (end >= 0) t = t[..end];
            t = t.Trim();
        }

        if (t.Length > 0 && t[0] != '{' && t[0] != '[')
        {
            var open = t.IndexOf('{');
            var close = t.LastIndexOf('}');
            if (open >= 0 && close > open) t = t[open..(close + 1)];
        }
        return t;
    }

    private static IEnumerable<string> Texts(JsonNode? node)
    {
        if (node?["parts"] is not JsonArray parts) yield break;
        foreach (var p in parts)
            if ((p?["text"] as JsonValue)?.GetValue<string>() is { } s && s.Length > 0)
                yield return s;
    }
}
