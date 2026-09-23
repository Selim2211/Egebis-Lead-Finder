using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace EgebisLeadFinder.Services;

/// <summary>
/// E-posta editorunden gelen HTML'i guvenli bir alt kumeye indirger ve gonderim
/// icin gorselleri cid referansina cevirir. Editor tarayicida calistigi icin gelen
/// HTML'e guvenilmez: izinli etiket/ozellik/stil disindaki her sey atilir.
/// </summary>
public static partial class EmailHtml
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "div", "p", "br", "b", "strong", "i", "em", "u", "span", "img", "a", "ul", "ol", "li", "blockquote"
    };

    /// <summary>Icerigiyle birlikte tamamen silinen etiketler.</summary>
    private static readonly HashSet<string> DroppedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "form", "input", "button", "select", "textarea",
        "svg", "math", "head", "title", "meta", "link", "template", "noscript"
    };

    private static readonly HashSet<string> AllowedStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "float", "width", "height", "max-width", "display", "vertical-align", "text-align",
        "margin", "margin-left", "margin-right", "margin-top", "margin-bottom",
        "font-weight", "font-style", "text-decoration"
    };

    [GeneratedRegex(@"^(?:https?://[^/]+)?/Email/Image/(\d+)(?:[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ImageSrcRegex();

    [GeneratedRegex(@"^[#\w\s.,%-]+$")]
    private static partial Regex SafeStyleValueRegex();

    /// <summary>Duz metni editorde gosterilecek HTML'e cevirir (satir sonlari korunur).</summary>
    public static string FromPlainText(string? text) =>
        WebUtility.HtmlEncode((text ?? string.Empty).Replace("\r\n", "\n")).Replace("\n", "<br>");

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        Clean(doc.DocumentNode);
        return doc.DocumentNode.InnerHtml.Trim();
    }

    /// <summary>Temizlenmis HTML'deki gorsel kimlikleri (gecis sirasina gore, tekrarsiz).</summary>
    public static List<int> ImageIds(string? sanitizedHtml)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(sanitizedHtml)) return ids;

        var doc = new HtmlDocument();
        doc.LoadHtml(sanitizedHtml);
        foreach (var img in doc.DocumentNode.Descendants("img"))
        {
            var id = ParseImageId(img.GetAttributeValue("src", ""));
            if (id is not null && !ids.Contains(id.Value)) ids.Add(id.Value);
        }
        return ids;
    }

    /// <summary>
    /// Gonderim HTML'i: gorsel src'leri cid'e cevrilir (cidFor null donerse gorsel atilir),
    /// govde e-posta istemcilerinde tutarli gorunsun diye yazi tipiyle sarilir.
    /// </summary>
    public static string ToEmailHtml(string sanitizedHtml, Func<int, string?> cidFor)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(sanitizedHtml);

        foreach (var img in doc.DocumentNode.Descendants("img").ToList())
        {
            var id = ParseImageId(img.GetAttributeValue("src", ""));
            var cid = id is null ? null : cidFor(id.Value);
            if (cid is null) { img.Remove(); continue; }
            img.SetAttributeValue("src", "cid:" + cid);
            img.SetAttributeValue("border", "0");
        }

        return "<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.6;color:#1f2937\">"
               + doc.DocumentNode.InnerHtml + "</div>";
    }

    public static int? ParseImageId(string? src)
    {
        var m = ImageSrcRegex().Match(src?.Trim() ?? "");
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    private static void Clean(HtmlNode node)
    {
        foreach (var child in node.ChildNodes.ToList())
        {
            switch (child.NodeType)
            {
                case HtmlNodeType.Comment:
                    child.Remove();
                    continue;
                case HtmlNodeType.Text:
                    continue;
                case HtmlNodeType.Element:
                    break;
                default:
                    child.Remove();
                    continue;
            }

            if (DroppedTags.Contains(child.Name))
            {
                child.Remove();
                continue;
            }

            Clean(child);

            if (!AllowedTags.Contains(child.Name))
            {
                // Bilinmeyen sarmalayici (font, section, o:p ...) atilir, icerigi kalir.
                foreach (var grand in child.ChildNodes.ToList())
                    node.InsertBefore(grand, child);
                child.Remove();
                continue;
            }

            CleanAttributes(child);

            if (child.Name.Equals("img", StringComparison.OrdinalIgnoreCase) && !child.Attributes.Contains("src"))
                child.Remove();
        }
    }

    private static void CleanAttributes(HtmlNode el)
    {
        var name = el.Name.ToLowerInvariant();

        foreach (var attr in el.Attributes.ToList())
        {
            var a = attr.Name.ToLowerInvariant();
            var keep = a switch
            {
                "style" => true,
                "src" when name == "img" => ParseImageId(attr.Value) is not null,
                "alt" when name == "img" => true,
                "width" or "height" when name == "img" => int.TryParse(attr.Value, out var n) && n is > 0 and <= 1200,
                "href" when name == "a" => IsSafeHref(attr.Value),
                _ => false
            };

            if (!keep) { attr.Remove(); continue; }

            if (a == "style")
            {
                var style = CleanStyle(attr.Value ?? string.Empty);
                if (style.Length == 0) attr.Remove();
                else attr.Value = style;
            }
            else if (a == "src")
            {
                attr.Value = $"/Email/Image/{ParseImageId(attr.Value)}";
            }
            else
            {
                // Tek tirnakli kaynaktaki " karakteri cift tirnakli ciktida ozelligi kirmasin.
                attr.Value = WebUtility.HtmlEncode(WebUtility.HtmlDecode(attr.Value));
            }
        }

        if (name == "a")
        {
            el.SetAttributeValue("target", "_blank");
            el.SetAttributeValue("rel", "noopener");
        }
    }

    private static bool IsSafeHref(string? href)
    {
        var h = WebUtility.HtmlDecode(href ?? "").Trim();
        return h.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || h.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanStyle(string style)
    {
        var parts = new List<string>();
        foreach (var decl in WebUtility.HtmlDecode(style).Split(';'))
        {
            var idx = decl.IndexOf(':');
            if (idx <= 0) continue;
            var prop = decl[..idx].Trim().ToLowerInvariant();
            var value = decl[(idx + 1)..].Trim();
            if (AllowedStyles.Contains(prop) && value.Length is > 0 and < 60 && SafeStyleValueRegex().IsMatch(value))
                parts.Add($"{prop}:{value}");
        }
        return string.Join(";", parts);
    }
}
