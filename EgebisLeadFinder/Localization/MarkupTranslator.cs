using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EgebisLeadFinder.Localization;

/// <summary>
/// Olusturulmus HTML, JS ve JSON ciktisindaki Turkce metinleri sozlukten Ingilizce'ye cevirir.
/// Yalnizca sozlukte karsiligi olan metinlere dokunur; kullanici verisi (firma adlari, mail govdeleri) korunur.
/// </summary>
public static class MarkupTranslator
{
    // Sirasiyla: yorum, script, style, textarea, etiket, metin.
    private static readonly Regex HtmlToken = new(
        @"<!--[\s\S]*?-->|<script\b[^>]*>[\s\S]*?</script>|<style\b[^>]*>[\s\S]*?</style>|<textarea\b[^>]*>[\s\S]*?</textarea>|<[^>]+>|[^<]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Attribute = new(
        "(?<name>[\\w:.-]+)\\s*=\\s*(?:\"(?<v>[^\"]*)\"|'(?<v>[^']*)')", RegexOptions.Compiled);

    // Formla gonderilen / islevsel degerler: cevrilirse form bozulur.
    private static readonly HashSet<string> SkipAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "value", "name", "id", "class", "href", "src", "action", "for", "style", "type", "method", "rel", "target",
        "data-val", "data-val-required", "content"
    };

    private static readonly Regex ScriptOpen = new(@"^<script\b([^>]*)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex JsLiteral = new(
        "\"(?<d>(?:[^\"\\\\\\r\\n]|\\\\.)*)\"|'(?<s>(?:[^'\\\\\\r\\n]|\\\\.)*)'|`(?<t>(?:[^`\\\\]|\\\\.)*)`",
        RegexOptions.Compiled);

    private static readonly Regex JsTemplateExpr = new(@"\$\{[^}]*\}", RegexOptions.Compiled);

    private static readonly Regex JsAssetRef = new(
        @"(?<pre>(?:src|href)\s*=\s*[""'])(?<path>/js/[^""'?#]+\.js)(?<q>\?[^""'#]*)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TagName = new(@"^<\s*(?<close>/?)\s*(?<name>[a-zA-Z][a-zA-Z0-9-]*)", RegexOptions.Compiled);
    private static readonly Regex NoTranslateMark = new(
        @"\bcontenteditable\s*=\s*[""']?(true|plaintext-only)?[""']?(?=[\s>/])|\btranslate\s*=\s*[""']?no\b|\bdata-no-i18n\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
        { "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr" };

    /// <summary>
    /// HTML metin düğümlerini ve başlık/ipucu niteliklerini çevirir. contenteditable (mail editörü), translate="no" ve
    /// data-no-i18n işaretli öğelerin içi kullanıcı içeriği sayılır ve olduğu gibi bırakılır.
    /// </summary>
    public static string TranslateHtml(string html)
    {
        var sb = new StringBuilder(html.Length + 256);
        string? skipTag = null;
        var skipDepth = 0;

        foreach (Match m in HtmlToken.Matches(html))
        {
            var token = m.Value;
            var isMarkup = token[0] == '<' && !token.StartsWith("<!--", StringComparison.Ordinal);

            if (skipTag is not null)
            {
                if (isMarkup)
                {
                    var inner = TagName.Match(token);
                    if (inner.Success && inner.Groups["name"].Value.Equals(skipTag, StringComparison.OrdinalIgnoreCase))
                    {
                        if (inner.Groups["close"].Length > 0) skipDepth--;
                        else if (!token.EndsWith("/>", StringComparison.Ordinal) && !VoidTags.Contains(skipTag)) skipDepth++;
                        if (skipDepth <= 0) skipTag = null;
                    }
                }
                sb.Append(token);
                continue;
            }

            if (token.StartsWith("<!--", StringComparison.Ordinal)) sb.Append(token);
            else if (token.StartsWith("<script", StringComparison.OrdinalIgnoreCase)) sb.Append(TranslateScript(token));
            else if (token.StartsWith("<style", StringComparison.OrdinalIgnoreCase)) sb.Append(token);
            else if (token.StartsWith("<textarea", StringComparison.OrdinalIgnoreCase)) sb.Append(TranslateTextareaOpen(token));
            else if (token[0] == '<')
            {
                var open = TagName.Match(token);
                if (open.Success && open.Groups["close"].Length == 0 && NoTranslateMark.IsMatch(token)
                    && !token.EndsWith("/>", StringComparison.Ordinal) && !VoidTags.Contains(open.Groups["name"].Value))
                {
                    skipTag = open.Groups["name"].Value;
                    skipDepth = 1;
                    sb.Append(token);
                }
                else sb.Append(TranslateTag(token));
            }
            else sb.Append(TranslateText(token));
        }
        return sb.ToString();
    }

    private static string TranslateText(string raw)
    {
        if (raw.IndexOf('&') < 0 && raw.IndexOf('%') < 0 && !HasLetter(raw)) return raw;
        var decoded = WebUtility.HtmlDecode(raw);
        var hit = Loc.TryTranslate(decoded);
        if (hit is null)
        {
            if (Loc.TrackMissing) Loc.Translate(decoded);
            return raw;
        }
        return Encode(hit);
    }

    private static string TranslateTag(string tag)
    {
        if (tag.StartsWith("</", StringComparison.Ordinal)) return tag;

        var isButtonInput = Regex.IsMatch(tag, @"^<input\b[^>]*\btype\s*=\s*[""']?(submit|button|reset)", RegexOptions.IgnoreCase);
        var isHtmlTag = tag.StartsWith("<html", StringComparison.OrdinalIgnoreCase);

        return Attribute.Replace(tag, m =>
        {
            var name = m.Groups["name"].Value;
            if (isHtmlTag && name.Equals("lang", StringComparison.OrdinalIgnoreCase))
                return $"lang=\"{Loc.Code}\"";
            if (SkipAttributes.Contains(name) && !(isButtonInput && name.Equals("value", StringComparison.OrdinalIgnoreCase)))
                return m.Value;
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || name.StartsWith("asp-", StringComparison.OrdinalIgnoreCase))
                return m.Value;

            var group = m.Groups["v"];
            var decoded = WebUtility.HtmlDecode(group.Value);
            var hit = Loc.TryTranslate(decoded);
            if (hit is null)
            {
                if (Loc.TrackMissing && IsVisibleAttribute(name)) Loc.Translate(decoded);
                return m.Value;
            }

            var quote = m.Value[group.Index - m.Index - 1];
            return m.Value[..(group.Index - m.Index)] + EncodeAttribute(hit, quote) + m.Value[(group.Index - m.Index + group.Length)..];
        });
    }

    private static bool IsVisibleAttribute(string name) =>
        name.Equals("title", StringComparison.OrdinalIgnoreCase) || name.Equals("placeholder", StringComparison.OrdinalIgnoreCase)
        || name.Equals("aria-label", StringComparison.OrdinalIgnoreCase) || name.Equals("alt", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("data-confirm", StringComparison.OrdinalIgnoreCase) || name.StartsWith("data-title", StringComparison.OrdinalIgnoreCase);

    private static string TranslateTextareaOpen(string token)
    {
        var close = token.IndexOf('>');
        if (close < 0) return token;
        return TranslateTag(token[..(close + 1)]) + token[(close + 1)..];
    }

    private static string TranslateScript(string token)
    {
        var open = ScriptOpen.Match(token);
        if (!open.Success) return token;
        var attrs = open.Groups[1].Value;
        if (Regex.IsMatch(attrs, @"type\s*=\s*[""']?(application/(json|ld\+json)|importmap)", RegexOptions.IgnoreCase)) return token;

        // Dis betik dosyasi cagrisi (<script src=...>) icerik tasimaz; yalniz surum eki.
        if (attrs.Contains("src", StringComparison.OrdinalIgnoreCase)) return TagLangSuffix(token);

        var bodyStart = open.Length;
        var bodyEnd = token.LastIndexOf("</script>", StringComparison.OrdinalIgnoreCase);
        if (bodyEnd < bodyStart) return token;
        return token[..bodyStart] + TranslateJs(token[bodyStart..bodyEnd]) + token[bodyEnd..];
    }

    /// <summary>/js/*.js dosyalari dile gore farkli icerik donduruldugu icin onbellek karismasin: adrese dil eki eklenir.</summary>
    public static string TagLangSuffix(string html) => JsAssetRef.Replace(html, m =>
    {
        var q = m.Groups["q"].Value;
        var sep = q.Length == 0 ? "?" : q + "&";
        return m.Groups["pre"].Value + m.Groups["path"].Value + sep + "l=" + Loc.Code;
    });

    /// <summary>JS kaynak kodundaki dizge sabitlerini ceviri sozlugunden degistirir.</summary>
    public static string TranslateJs(string js) => JsLiteral.Replace(js, m =>
    {
        if (m.Groups["t"].Success) return TranslateTemplateLiteral(m);

        var isDouble = m.Groups["d"].Success;
        var inner = isDouble ? m.Groups["d"].Value : m.Groups["s"].Value;
        if (inner.Length < 2) return m.Value;

        var text = JsUnescape(inner);
        var hit = Loc.TryTranslate(text);
        // HTML parçası içeren dizgeler (düğme şablonları): metin düğümleri ve başlıklar ayrı çevrilir.
        if (hit is null && text.Contains('<') && text.Contains('>'))
        {
            var html = TranslateHtml(text);
            if (!string.Equals(html, text, StringComparison.Ordinal)) hit = html;
        }
        if (hit is null) return m.Value;
        var q = isDouble ? '"' : '\'';
        return q + JsEscape(hit, q) + q;
    });

    private static string TranslateTemplateLiteral(Match m)
    {
        var inner = m.Groups["t"].Value;
        var exprs = new List<string>();
        var keyed = JsTemplateExpr.Replace(inner, e =>
        {
            exprs.Add(e.Value);
            return "{" + (exprs.Count - 1) + "}";
        });

        string? hit = exprs.Count == 0 ? Loc.TryTranslate(JsUnescape(inner)) : Loc.TryTranslateExactKey(keyed);
        if (hit is null) return m.Value;
        if (exprs.Count > 0)
            hit = Regex.Replace(hit, @"\{(\d+)\}", pm =>
                int.TryParse(pm.Groups[1].Value, out var i) && i < exprs.Count ? exprs[i] : pm.Value);
        return "`" + hit.Replace("\\", "\\\\").Replace("`", "\\`") + "`";
    }

    /// <summary>JSON govdesindeki dizge degerleri; karsiligi olmayanlar degismez.</summary>
    public static string TranslateJson(string json) => Regex.Replace(json, "\"(?<v>(?:[^\"\\\\]|\\\\.)*)\"(?<key>\\s*:)?", m =>
    {
        if (m.Groups["key"].Success) return m.Value;
        var raw = m.Groups["v"].Value;
        if (raw.Length < 2) return m.Value;
        string text;
        try { text = System.Text.Json.JsonSerializer.Deserialize<string>(m.Value) ?? raw; }
        catch (System.Text.Json.JsonException) { return m.Value; }

        var hit = Loc.TryTranslate(text);
        return hit is null ? m.Value : System.Text.Json.JsonSerializer.Serialize(hit, JsonOptions);
    });

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string JsUnescape(string s) => s.Contains('\\')
        ? Regex.Replace(s, @"\\(.)", m => m.Groups[1].Value switch { "n" => "\n", "t" => "\t", var c => c })
        : s;

    private static string JsEscape(string s, char quote) =>
        s.Replace("\\", "\\\\").Replace(quote.ToString(), "\\" + quote).Replace("\r", "").Replace("\n", "\\n");

    private static bool HasLetter(string s)
    {
        foreach (var ch in s) if (char.IsLetter(ch)) return true;
        return false;
    }

    private static string Encode(string s)
    {
        // Ingilizce metin ASCII agirlikli; yalniz HTML'e ozel karakterler kacirilir.
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
            sb.Append(ch switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", _ => ch.ToString() });
        return sb.ToString();
    }

    private static string EncodeAttribute(string s, char quote)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            if (ch == '&') sb.Append("&amp;");
            else if (ch == '<') sb.Append("&lt;");
            else if (ch == '>') sb.Append("&gt;");
            else if (ch == quote) sb.Append(quote == '"' ? "&quot;" : "&#39;");
            else sb.Append(ch);
        }
        return sb.ToString();
    }
}
