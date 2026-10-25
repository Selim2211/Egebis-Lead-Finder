using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>OpenAI uyumlu chat completions saglayicisi (NVIDIA, Groq).</summary>
public sealed record ChatProvider(
    string Id, string Label, string Endpoint, string KeySetting, string ModelSetting,
    string DefaultModel, string UsageName, int? RequestTokenBudget = null);

/// <summary>
/// Yapay zeka saglayicisi secimi. Varsayilan Gemini; Ayarlar'dan OpenAI uyumlu bir saglayici
/// (NVIDIA, Groq) secilebilir. Cagri noktalari Gemini bicimli istek uretmeye devam eder;
/// istek/yanit <see cref="OpenAiChatAdapter"/> ile tasima katmaninda cevrilir.
/// </summary>
public static class AiProviders
{
    public const string Gemini = "gemini";
    public const string Nvidia = "nvidia";
    public const string Groq = "groq";

    public static readonly ChatProvider NvidiaProvider = new(
        Nvidia, "NVIDIA", "https://integrate.api.nvidia.com/v1/chat/completions",
        SettingKeys.NvidiaApiKey, SettingKeys.NvidiaModel, "nvidia/nemotron-3.5-lightning-30b-a3b", "Nvidia");

    public static readonly ChatProvider GroqProvider = new(
        Groq, "Groq", "https://api.groq.com/openai/v1/chat/completions",
        SettingKeys.GroqApiKey, SettingKeys.GroqModel, "openai/gpt-oss-120b, openai/gpt-oss-20b", "Groq",
        // Ucretsiz katman dakikada 8000 token; tek istek (girdi + cikti payi) bunu asarsa 413 doner.
        RequestTokenBudget: 7000);

    /// <summary>Secili OpenAI uyumlu saglayici; Gemini seciliyse null.</summary>
    public static async Task<ChatProvider?> ChatProviderAsync(ISettingsService settings, CancellationToken ct) =>
        (await settings.GetAsync(SettingKeys.AiProvider, ct))?.Trim().ToLowerInvariant() switch
        {
            Nvidia => NvidiaProvider,
            Groq => GroqProvider,
            _ => null
        };

    /// <summary>Secili saglayicinin API anahtari (yalnizca Ayarlar ekranindan).</summary>
    public static async Task<string?> ActiveKeyAsync(ISettingsService settings, CancellationToken ct) =>
        await ChatProviderAsync(settings, ct) is { } p
            ? await settings.GetAsync(p.KeySetting, ct)
            : await settings.GetAsync(SettingKeys.GeminiApiKey, ct);

    /// <summary>Ilk (oncelikli) model.</summary>
    public static async Task<string> ModelAsync(ChatProvider provider, ISettingsService settings, CancellationToken ct) =>
        (await ModelListAsync(provider, settings, ct))[0];

    /// <summary>Model havuzu: Ayarlar'da virgulle yazilan sirali liste; bossa saglayicinin varsayilani.</summary>
    public static async Task<IReadOnlyList<string>> ModelListAsync(ChatProvider provider, ISettingsService settings, CancellationToken ct)
    {
        var list = ChatModelPool.Parse(await settings.GetAsync(provider.ModelSetting, ct));
        return list.Count > 0 ? list : ChatModelPool.Parse(provider.DefaultModel);
    }
}

/// <summary>
/// Ayni hesabin modelleri arasinda yedekleme: Groq'ta sinirlar model basinadir. Bir model
/// dakikalik/gunluk sinira takilinca ya da kaldirilmissa bir sure "dolu" isaretlenir ve
/// siradaki modele gecilir. Durum bellekte tutulur (uygulama yeniden baslayinca sifirlanir).
/// </summary>
public static class ChatModelPool
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> BlockedUntil = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Parse(string? setting) =>
        (setting ?? string.Empty)
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Bu cagrida denenmemis ve dolu olmayan ilk model; yoksa null.</summary>
    public static string? Next(string provider, IReadOnlyList<string> models, ICollection<string> tried, DateTimeOffset now) =>
        models.FirstOrDefault(m => !tried.Contains(m) && !IsBlocked(provider, m, now));

    public static bool IsBlocked(string provider, string model, DateTimeOffset now) =>
        BlockedUntil.TryGetValue(provider + "|" + model, out var until) && until > now;

    public static void Block(string provider, string model, TimeSpan duration, DateTimeOffset now) =>
        BlockedUntil[provider + "|" + model] = now + duration;

    /// <summary>Gunluk sinir (tokens/requests per day) mi? Beklemek dakikalar/saatler surer.</summary>
    public static bool IsDailyLimit(string body) =>
        body.Contains("per day", StringComparison.OrdinalIgnoreCase)
        || body.Contains("(TPD)", StringComparison.Ordinal) || body.Contains("(RPD)", StringComparison.Ordinal);

    /// <summary>Model kaldirilmis / bulunamiyor mu?</summary>
    public static bool IsModelGone(int status, string body) =>
        status is 404 or 410
        || (status == 400 && Regex.IsMatch(body, "decommissioned|model_not_found|does not exist", RegexOptions.IgnoreCase));

    /// <summary>Groq hata metnindeki "try again in 7m12.5s" bekleme suresi.</summary>
    public static TimeSpan? ParseTryAgain(string body)
    {
        var m = Regex.Match(body, @"try again in (?:(\d+)h)?(?:(\d+)m(?!s))?(?:([\d.]+)s)?(?:([\d.]+)ms)?", RegexOptions.IgnoreCase);
        if (!m.Success || m.Length <= "try again in ".Length) return null;
        double D(int i) => m.Groups[i].Success ? double.Parse(m.Groups[i].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        var t = TimeSpan.FromHours(D(1)) + TimeSpan.FromMinutes(D(2)) + TimeSpan.FromSeconds(D(3)) + TimeSpan.FromMilliseconds(D(4));
        return t > TimeSpan.Zero ? t : null;
    }

    /// <summary>429/413/kaldirilmis model icin "dolu" suresi.</summary>
    public static TimeSpan BlockDuration(int status, string body, TimeSpan? retryAfter)
    {
        if (IsModelGone(status, body)) return TimeSpan.FromHours(24);
        var hinted = retryAfter ?? ParseTryAgain(body);
        if (IsDailyLimit(body)) return hinted is { } h && h > TimeSpan.FromMinutes(1) ? h : TimeSpan.FromMinutes(30);
        return hinted ?? TimeSpan.FromSeconds(60);
    }

    internal static void Reset() => BlockedUntil.Clear();
}

/// <summary>Gemini bicimli istegi OpenAI chat istegine, chat yanitini Gemini zarfina cevirir.</summary>
public static class OpenAiChatAdapter
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

    public static string ToChatRequest(string geminiPayloadJson, string model, bool thinkingOff = true, bool jsonMode = false,
        int? tokenBudget = null)
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

        // Kucuk modeller uzun talimatin basindaki dil kuralini, ozellikle kaynak site yabanci dildeyse,
        // unutup Ingilizce yaziyor; kural talimatin sonunda tekrarlanir.
        if (system.Contains("ÇIKTI DİLİ: İngilizce", StringComparison.Ordinal))
            system += "\n\nHATIRLATMA: JSON içindeki tüm açıklama/özet/gerekçe metinlerini İNGİLİZCE yaz.";
        else if (system.Contains("Türkçe yaz", StringComparison.Ordinal))
            system += "\n\nHATIRLATMA: Kaynak metin hangi dilde olursa olsun (İngilizce, Almanca vb.), JSON içindeki " +
                      "tüm açıklama/özet/gerekçe metinlerini TÜRKÇE yaz. Alan adları şemadaki gibi kalsın.";

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

        // Token butcesi varsa (Groq ucretsiz katman) cikti payi kucultulur, en uzun mesaj
        // (genelde site/kaynak metni) butceye sigacak kadar kirpilir.
        var maxTokens = MaxTokens;
        if (tokenBudget is { } budget)
        {
            maxTokens = Math.Min(MaxTokens, Math.Max(1024, budget / 4));
            FitToBudget(messages, budget - maxTokens);
        }

        var temperature = gen?["temperature"] is JsonValue t && t.TryGetValue<double>(out var d) ? d : 0.2;
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = temperature,
            ["max_tokens"] = maxTokens,
            ["stream"] = false
        };
        if (thinkingOff) AddThinkingOff(body, model);
        if (jsonMode && wantsJson) body["response_format"] = new JsonObject { ["type"] = "json_object" };
        return body.ToJsonString();
    }

    /// <summary>Turkce metinde ~3 karakter = 1 token (temkinli tahmin).</summary>
    public static int EstimateTokens(string text) => text.Length / 3 + 1;

    /// <summary>Toplam tahmini token butceyi asiyorsa en uzun mesaji sonundan kirpar.</summary>
    public static void FitToBudget(JsonArray messages, int inputBudget)
    {
        string Content(JsonNode? m) => (m?["content"] as JsonValue)?.GetValue<string>() ?? string.Empty;
        var total = messages.Sum(m => EstimateTokens(Content(m)));
        if (total <= inputBudget) return;

        // Sistem mesajinin sonunda JSON semasi ve dil kurali var; once kullanici mesaji (kaynak metin) kirpilir.
        var candidates = messages.Where(m => (m?["role"] as JsonValue)?.GetValue<string>() != "system").ToList();
        if (candidates.Count == 0) candidates = messages.ToList();
        var longest = candidates.OrderByDescending(m => Content(m).Length).First()!;
        var text = Content(longest);
        var keepTokens = EstimateTokens(text) - (total - inputBudget);
        var keepChars = Math.Max(500, keepTokens * 3);
        if (keepChars < text.Length)
            longest["content"] = text[..keepChars] + "\n[... metin uzunluk sınırı nedeniyle kısaltıldı ...]";
    }

    /// <summary>OpenAI chat yanitini, mevcut ExtractText'in bekledigi Gemini zarfina cevirir.</summary>
    private static readonly JsonSerializerOptions RelaxedJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ToGeminiResponse(string chatResponseJson, string? aiModel = null)
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
            // Hangi modelin urettigi analizle birlikte saklansin (firma detayinda gosterilir).
            if (aiModel is not null)
            {
                try
                {
                    if (JsonNode.Parse(text) is JsonObject obj)
                    {
                        obj["aiModel"] = aiModel;
                        text = obj.ToJsonString(RelaxedJson);
                    }
                }
                catch (JsonException) { }
            }
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
