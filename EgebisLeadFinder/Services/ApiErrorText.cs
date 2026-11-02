using System.Text.Json;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Saglayicinin hata yanitini kullaniciya gosterilecek kisa, anlasilir bir cumleye cevirir
/// (acilis uyarisi ve API Kullanimi ekrani). Saglayicinin kendi mesaji da kisaltilarak eklenir.
/// </summary>
public static class ApiErrorText
{
    public static string Describe(int status, string? body)
    {
        var raw = ProviderMessage(body);
        var lower = (raw ?? body ?? string.Empty).ToLowerInvariant();

        var summary = status switch
        {
            402 => "Kredi bitti: ön ödemeli bakiye tükendi",
            401 => "API anahtarı geçersiz veya süresi dolmuş",
            403 when lower.Contains("credit") || lower.Contains("quota") || lower.Contains("balance")
                => "Kredi/kota yetersiz",
            403 => "Erişim reddedildi: anahtarın bu işleme yetkisi yok",
            429 when ChatModelPool.IsDailyLimit(lower) || lower.Contains("per day") || lower.Contains("daily")
                => "Günlük kullanım sınırı doldu",
            429 => "Kullanım sınırı/kota doldu",
            _ => "Servis hata döndürdü"
        };

        var text = $"{summary} (HTTP {status})";
        if (!string.IsNullOrWhiteSpace(raw))
            text += ": " + (raw.Length > 160 ? raw[..160] + "…" : raw);
        return text;
    }

    /// <summary>JSON hata govdesinden "message" alanini cikarir (Gemini, OpenAI uyumlu, Serper bicimleri).</summary>
    public static string? ProviderMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var err))
                {
                    if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                        return m.GetString()?.Trim();
                    if (err.ValueKind == JsonValueKind.String) return err.GetString()?.Trim();
                }
                if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String) return msg.GetString()?.Trim();
                if (root.TryGetProperty("detail", out var det) && det.ValueKind == JsonValueKind.String) return det.GetString()?.Trim();
            }
        }
        catch (JsonException) { }
        var t = body.Trim();
        return t.Length > 160 ? t[..160] : t;
    }
}
