using System.Text.RegularExpressions;

namespace EgebisLeadFinder.Services.News;

/// <summary>Bir haberin "parmak izi": başlık + özetten çıkan kelime kökleri ve özel isimler.</summary>
public sealed record NewsPrint(HashSet<string> Tokens, HashSet<string> Proper);

/// <summary>Benzerlik hesabında kullanılan haber: kimlik, yayın zamanı ve parmak izi.</summary>
public sealed record StoryItem(long Id, DateTime PublishedAt, NewsPrint Print);

/// <summary>
/// Aynı olayı anlatan farklı haberleri (farklı site, farklı başlık) bulur.
/// <para>
/// 1) Başlık + özet kelimelere ayrılır; Türkçe ekler atılır (ilk 5 harf kök sayılır: "fabrikalık" → "fabri"),
///    "ve, ile, yeni" gibi boş kelimeler çıkarılır, sayılar ayrı tutulur ("5", "439").
/// 2) Özel isimler ayrıca işaretlenir: kesme işaretli (ASELSAN'dan), tamamı büyük harf (ABD) ya da cümle ortasında büyük harfle başlayan kelimeler.
/// 3) İki haberin ortak kelimeleri nadirliklerine göre puanlanır (çok geçen "yatırım, fabrika" az, "ASELSAN, Malatya, 66" çok sayılır).
/// 4) Aynı haber sayılması için en az bir ortak özel isim şarttır (Honda'nın ABD fabrikası ile Uludağ'ın Malatya fabrikası karışmaz);
///    ardından ağırlıklı benzerlik eşiği ya da "2 ortak özel isim + ortak sayı" kuralı aranır.
/// 5) Yayın zamanları 72 saatten uzaksa aynı haber sayılmaz. Benzerlik geçişlidir (A~B, B~C ⇒ hepsi tek haber).
/// </para>
/// </summary>
public static class NewsSimilarity
{
    public const double Threshold = 0.45;
    public static readonly TimeSpan Window = TimeSpan.FromHours(72);

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "ve", "ile", "bir", "icin", "da", "de", "bu", "su", "o", "en", "cok", "daha", "gibi", "olarak", "olan",
        "mi", "mu", "yeni", "dev", "haber", "haberi", "son", "ilk", "her", "kadar", "sonra", "once", "yil", "ne",
        "nasil", "diye", "ise", "ama", "veya", "hem", "tum", "buyuk"
    };

    private static readonly Regex Words = new(@"[0-9]+(?:[.,][0-9]+)*|[\p{L}]+(?:['’][\p{L}]+)?", RegexOptions.Compiled);
    private static readonly Regex LetterWords = new(@"[\p{L}]+(?:['’][\p{L}]+)?", RegexOptions.Compiled);

    public static NewsPrint Print(string? title, string? summary)
    {
        title ??= string.Empty;
        var extra = string.IsNullOrWhiteSpace(summary) || summary.Trim() == title.Trim() ? string.Empty : summary;
        var tokens = Tokens(title + " " + extra);
        var proper = Propers(title);
        proper.UnionWith(Propers(extra));
        proper.IntersectWith(tokens);
        return new NewsPrint(tokens, proper);
    }

    public static HashSet<string> Tokens(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Words.Matches(text ?? string.Empty))
        {
            var w = m.Value;
            var cut = w.IndexOfAny(new[] { '\'', '’' });
            if (cut > 0) w = w[..cut];
            w = TurkishText.Normalize(w);
            if (w.Length == 0 || Stop.Contains(w)) continue;
            if (char.IsDigit(w[0]))
            {
                set.Add("#" + w.Replace(".", string.Empty).Replace(",", string.Empty));
                continue;
            }
            if (w.Length < 3) continue;
            set.Add(w.Length > 5 ? w[..5] : w);
        }
        return set;
    }

    private static HashSet<string> Propers(string text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var words = LetterWords.Matches(text ?? string.Empty).Select(m => m.Value).ToList();
        if (words.Count == 0) return result;
        // Her kelimesi büyük harfle başlayan başlıklarda ("Tekstil Devi İflas Etti") yalnızca kesmeli/tamamı büyük kelimeler sayılır.
        var titleCase = words.Count(w => char.IsUpper(w[0])) > words.Count * 0.6;
        for (var i = 0; i < words.Count; i++)
        {
            var w = words[i];
            var cut = w.IndexOfAny(new[] { '\'', '’' });
            var b = cut > 0 ? w[..cut] : w;
            if (b.Length < 2 || !char.IsUpper(b[0])) continue;
            var allCaps = b.All(c => !char.IsLetter(c) || char.IsUpper(c));
            if (cut > 0 || allCaps || (!titleCase && i > 0)) result.UnionWith(Tokens(b));
        }
        return result;
    }

    /// <summary>Kelime nadirliği: haberlerin çoğunda geçen kelime düşük, az geçen yüksek ağırlık alır.</summary>
    public static Dictionary<string, double> Idf(IEnumerable<NewsPrint> corpus)
    {
        var list = corpus.ToList();
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in list)
            foreach (var t in p.Tokens)
                df[t] = df.TryGetValue(t, out var c) ? c + 1 : 1;
        var n = list.Count;
        return df.ToDictionary(kv => kv.Key, kv => Math.Log((n + 1) / (kv.Value + 0.5)), StringComparer.Ordinal);
    }

    private static double Weight(string token, Dictionary<string, double> idf) =>
        idf.TryGetValue(token, out var w) ? w : Math.Log((idf.Count + 2) / 0.5);

    /// <summary>0–1 arası benzerlik; ortak özel isim yoksa 0.</summary>
    public static double Score(NewsPrint a, NewsPrint b, Dictionary<string, double> idf)
    {
        if (a.Tokens.Count == 0 || b.Tokens.Count == 0) return 0;
        var shared = a.Tokens.Where(b.Tokens.Contains).ToList();
        if (shared.Count < 3) return 0;
        var sharedProper = shared.Count(t => a.Proper.Contains(t) && b.Proper.Contains(t));
        if (sharedProper == 0) return 0;

        var w = shared.Sum(t => Weight(t, idf)) /
                Math.Min(a.Tokens.Sum(t => Weight(t, idf)), b.Tokens.Sum(t => Weight(t, idf)));
        var sharedNumber = shared.Any(t => t[0] == '#');
        if (sharedProper >= 2 && shared.Count >= 4 && (sharedNumber || w >= 0.3)) return Math.Max(w, Threshold);
        return Math.Min(1, w);
    }

    public static bool SameStory(StoryItem a, StoryItem b, Dictionary<string, double> idf) =>
        (a.PublishedAt - b.PublishedAt).Duration() <= Window && Score(a.Print, b.Print, idf) >= Threshold;

    /// <summary>Haberleri olaylara göre gruplar (geçişli). Yalnızca 2+ haberli gruplar döner.</summary>
    public static List<List<StoryItem>> Clusters(IReadOnlyList<StoryItem> items, Dictionary<string, double>? idf = null)
    {
        idf ??= Idf(items.Select(i => i.Print));
        var parent = Enumerable.Range(0, items.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }

        for (var i = 0; i < items.Count; i++)
            for (var j = 0; j < i; j++)
                if (Find(i) != Find(j) && SameStory(items[i], items[j], idf))
                    parent[Find(i)] = Find(j);

        return Enumerable.Range(0, items.Count).GroupBy(Find)
            .Where(g => g.Count() > 1).Select(g => g.Select(i => items[i]).ToList()).ToList();
    }

    /// <summary>Yeni haber, listedeki haberlerden biriyle aynı olaysa onu döner.</summary>
    public static StoryItem? FindSame(StoryItem candidate, IEnumerable<StoryItem> known, Dictionary<string, double> idf)
    {
        StoryItem? best = null;
        var bestScore = 0d;
        foreach (var k in known)
        {
            if ((k.PublishedAt - candidate.PublishedAt).Duration() > Window) continue;
            var s = Score(candidate.Print, k.Print, idf);
            if (s >= Threshold && s > bestScore) { best = k; bestScore = s; }
        }
        return best;
    }
}
