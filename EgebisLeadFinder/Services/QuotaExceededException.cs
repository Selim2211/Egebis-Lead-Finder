namespace EgebisLeadFinder.Services;

/// <summary>
/// Saglayicinin gunluk/aylik kotasi bitti. Dakikalik hiz limitinden farkli olarak
/// beklemek ise yaramaz; akis durdurulur ve kullaniciya bildirilir.
/// </summary>
public class QuotaExceededException : InvalidOperationException
{
    public QuotaExceededException(string provider)
        : base($"{provider} kotası doldu. Ayarlar'daki API anahtarını / faturalandırma planını kontrol edin.")
    {
        Provider = provider;
    }

    public string Provider { get; }

    /// <summary>
    /// 429 govdesi hiz limiti mi kota mi? Gemini kota asiminda "quota" / "free_tier"
    /// gibi ifadeler doner; dakikalik limitte ise genelde retryDelay bildirilir.
    /// </summary>
    public static bool LooksLikeQuota(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;

        var text = body.ToLowerInvariant();
        return text.Contains("free_tier")
            || text.Contains("exceeded your current quota")
            || text.Contains("quota exceeded")
            || text.Contains("billing")
            || (text.Contains("quota") && !text.Contains("retrydelay"));
    }
}
