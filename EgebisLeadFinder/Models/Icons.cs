using Microsoft.AspNetCore.Html;

namespace EgebisLeadFinder.Models;

/// <summary>
/// Emoji yerine kullanilan tek renkli cizgi ikonlar (uygulamadaki diger SVG'lerle ayni stil:
/// 24'luk tuval, 2px cizgi, yuvarlak uclar). Renk metinden gelir (currentColor).
/// </summary>
public static class Icons
{
    private static HtmlString Svg(string body, int size, string? css) => new(
        $"<svg class=\"eg-icon{(css is null ? "" : " " + css)}\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" " +
        $"stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{body}</svg>");

    /// <summary>Hedef: "Biz ne arıyoruz?" ve segmentler.</summary>
    public static HtmlString Target(int size = 14, string? css = null) =>
        Svg("<circle cx=\"12\" cy=\"12\" r=\"9\"/><circle cx=\"12\" cy=\"12\" r=\"5\"/><circle cx=\"12\" cy=\"12\" r=\"1\"/>", size, css);

    /// <summary>Yapay zeka ile otomatik uretim.</summary>
    public static HtmlString Sparkles(int size = 14, string? css = null) =>
        Svg("<path d=\"M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z\"/><path d=\"M19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8z\"/>", size, css);

    /// <summary>Kapat / kaldir.</summary>
    public static HtmlString X(int size = 14, string? css = null) =>
        Svg("<path d=\"M18 6 6 18\"/><path d=\"m6 6 12 12\"/>", size, css);
}
