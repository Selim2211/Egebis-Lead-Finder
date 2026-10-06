using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EgebisLeadFinder.Localization;

/// <summary>
/// Arayuz dili. Kaynak dil Turkce'dir; Ingilizce secildiginde Turkce metinler sozlukten (Resources/i18n/*.en.json)
/// cevrilir. Sozlukte olmayan metin oldugu gibi kalir. Anahtarda {0}, {1} varsa kalip (sablon) eslesmesi yapilir:
/// "{0} firma bulundu" -> "{0} companies found". Yakalanan parcalar da ayrica cevrilir.
/// </summary>
public static class Loc
{
    public const string CookieName = "eg-lang";
    public const string English = "en";
    public const string Turkish = "tr";

    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal);
    private static readonly List<Template> Templates = new();
    private static readonly ConcurrentDictionary<string, byte> Missing = new(StringComparer.Ordinal);
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?;])\s+", RegexOptions.Compiled);
    private static bool _loaded;
    private static readonly object Gate = new();

    private sealed record Template(Regex Pattern, string Value, int LiteralLength);

    /// <summary>Gelistirme sirasinda cevrilmemis metinleri toplar.</summary>
    public static bool TrackMissing { get; set; }

    public static bool IsEnglish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == English;

    public static string Code => IsEnglish ? English : Turkish;

    /// <summary>Yapay zekâ metinlerinin yazilacagi dil (istemde kullanilir).</summary>
    public static string AiLanguage => IsEnglish ? "İngilizce" : "Türkçe";

    /// <summary>
    /// Yapay zeka istemini arayuz diline uyarlar. Ingilizce secildiyse "Turkce yaz" kurallari Ingilizce'ye cevrilir ve
    /// cikti dili talimati eklenir; arama terimi / unvan listeleri hedef pazarin dilinde kalir.
    /// </summary>
    public static string Prompt(string prompt)
    {
        if (!IsEnglish) return prompt;
        var p = prompt
            .Replace("Tüm metin alanlarını Türkçe yaz", "Tüm metin alanlarını İngilizce yaz")
            .Replace("Tüm metinleri Türkçe yaz", "Tüm metinleri İngilizce yaz")
            .Replace("- Türkçe yaz.", "- İngilizce yaz.")
            .Replace("\"Sayın {CONTACT_NAME},\" ile başla", "\"Dear {CONTACT_NAME},\" ile başla")
            .Replace("\"Saygılarımızla,\" ve alt satırda", "\"Kind regards,\" ve alt satırda");
        return p + "\n\nÇIKTI DİLİ: İngilizce. Kullanıcıya gösterilecek tüm açıklama, özet, gerekçe, öneri, risk ve e-posta metinlerini İngilizce yaz; " +
               "yukarıdaki \"Türkçe yaz\" talimatlarını bu konuda yok say. Arama terimi, anahtar kelime ve hedef unvan listeleri hedef pazarın dilinde kalabilir.";
    }

    public static string NormalizeCode(string? code) =>
        string.Equals(code, English, StringComparison.OrdinalIgnoreCase) ? English : Turkish;

    /// <summary>Metni gecerli dile cevirir; Turkce secili ya da karsiligi yoksa aynen doner.</summary>
    public static string T(string text) => IsEnglish ? Translate(text) : text;

    /// <summary>Verilen dile gore cevirir (UI kulturunden bagimsiz).</summary>
    public static string Translate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        EnsureLoaded();
        var hit = TryTranslate(text, 0);
        if (hit is not null) return hit;
        if (TrackMissing && HasLetters(text)) Missing.TryAdd(Normalize(text), 0);
        return text;
    }

    /// <summary>Basindaki/sonundaki bosluklari koruyarak cevirir; karsiligi yoksa null.</summary>
    public static string? TryTranslate(string text, int depth = 0)
    {
        if (string.IsNullOrEmpty(text)) return null;
        EnsureLoaded();

        var start = 0;
        var end = text.Length;
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        if (start == end) return null;

        var core = text.Substring(start, end - start);
        var key = Normalize(core);

        string? translated = null;
        if (Exact.TryGetValue(key, out var exact) && !Placeholder.IsMatch(key)) translated = exact;
        else if (depth < 4)
        {
            foreach (var template in Templates)
            {
                var m = template.Pattern.Match(key);
                if (!m.Success) continue;
                translated = Placeholder.Replace(template.Value, pm =>
                {
                    var group = m.Groups["g" + pm.Groups[1].Value];
                    if (!group.Success) return pm.Value;
                    return TryTranslate(group.Value, depth + 1) ?? group.Value;
                });
                break;
            }
        }

        if (translated is null && depth == 0) translated = TrySentences(core);
        if (translated is null) return null;
        return text.Substring(0, start) + translated + text.Substring(end);
    }

    // Birden cok cumleli mesajlar (ör. kayit mesaji + ek uyarilar): cumle cumle cevrilir.
    private static string? TrySentences(string core)
    {
        var parts = SentenceSplit.Split(core);
        if (parts.Length < 2) return null;
        var any = false;
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            var hit = TryTranslate(part, 1);
            if (hit is not null) any = true;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(hit ?? part);
        }
        return any ? sb.ToString() : null;
    }

    /// <summary>JS sablon dizgisi (${..} yerlerine {0},{1}) icin birebir arama.</summary>
    public static string? TryTranslateExactKey(string key)
    {
        EnsureLoaded();
        return Exact.TryGetValue(Normalize(key), out var value) ? value : null;
    }

    public static IReadOnlyCollection<string> MissingTexts => Missing.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public static void ClearMissing() => Missing.Clear();

    public static string Normalize(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    private static bool HasLetters(string s)
    {
        foreach (var ch in s) if (char.IsLetter(ch)) return true;
        return false;
    }

    /// <summary>Resources/i18n altindaki tum *.en.json dosyalarini yukler (bir kez).</summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Gate)
        {
            if (_loaded) return;
            var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "i18n");
            if (Directory.Exists(dir))
                foreach (var file in Directory.EnumerateFiles(dir, "*.en.json").OrderBy(f => f, StringComparer.Ordinal))
                    Add(ReadFile(file));
            _loaded = true;
        }
    }

    // Tekrarlanan anahtarlarda sonuncusu gecerli olur (JsonDocument hata vermez).
    private static List<KeyValuePair<string, string>> ReadFile(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return doc.RootElement.EnumerateObject().Select(p => new KeyValuePair<string, string>(p.Name, p.Value.GetString() ?? string.Empty)).ToList();
    }

    /// <summary>Testler ve dosya disi kaynaklar icin sozluge dogrudan ekleme.</summary>
    public static void Add(IEnumerable<KeyValuePair<string, string>> entries)
    {
        foreach (var (tr, en) in entries)
        {
            var key = Normalize(tr);
            if (key.Length == 0 || en is null) continue;
            Exact[key] = en;

            if (!Placeholder.IsMatch(key)) continue;
            var pattern = new StringBuilder("^");
            var literal = 0;
            var last = 0;
            foreach (Match m in Placeholder.Matches(key))
            {
                var chunk = key.Substring(last, m.Index - last);
                pattern.Append(Regex.Escape(chunk));
                literal += chunk.Length;
                pattern.Append("(?<g").Append(m.Groups[1].Value).Append(">.+?)");
                last = m.Index + m.Length;
            }
            var tail = key.Substring(last);
            pattern.Append(Regex.Escape(tail)).Append('$');
            literal += tail.Length;
            Templates.Add(new Template(new Regex(pattern.ToString(), RegexOptions.Compiled | RegexOptions.Singleline), en, literal));
        }
        Templates.Sort((a, b) => b.LiteralLength.CompareTo(a.LiteralLength));
    }
}
