using System.Text.RegularExpressions;

namespace EgebisLeadFinder.Services;

public enum ProducerKind
{
    /// <summary>Kural katmani karar veremedi (yapay zeka bakar).</summary>
    Unclear,

    /// <summary>Sitede somut uretim kaniti var (m2, kapasite, makine parki, "uretiyoruz"...).</summary>
    Producer,

    /// <summary>Sitede bayi / ithalatci / e-ticaret isareti var, uretim kaniti yok.</summary>
    Trader
}

/// <summary>Bir sitenin uretici/bayi kanitlari. Alintilar sitenin kendi metninden aynen alinir.</summary>
public sealed record ProducerEvidence(
    ProducerKind Kind,
    int ProducerScore,
    int TraderScore,
    IReadOnlyList<string> ProducerQuotes,
    IReadOnlyList<string> TraderQuotes)
{
    public static ProducerEvidence None { get; } =
        new(ProducerKind.Unclear, 0, 0, Array.Empty<string>(), Array.Empty<string>());

    /// <summary>Sonuc ekraninda gosterilecek tek alinti (kind'a gore uretim ya da bayi kaniti).</summary>
    public string? BestQuote => Kind == ProducerKind.Trader
        ? TraderQuotes.FirstOrDefault()
        : ProducerQuotes.FirstOrDefault();
}

/// <summary>
/// Kural katmani: firma sitesinin metninden "uretici mi, bayi/ithalatci/e-ticaret mi" kanitlarini cikarir.
/// Arastirma notlari: ISO/CE/ihracat iddiasi zayif kanittir (bayiler de gosterir); asil kanit somut uretim bilgisidir
/// (kapali alan m2, uretim kapasitesi, makine parki, kapasite raporu). Anahtar kelimeden cok CUMLENIN YONU onemlidir:
/// "uretiyoruz" uretimdir, "ithal ediyoruz" / "X'in yetkili bayisiyiz" ticarettir; "bayilerimiz" ise marka sahibini gosterir
/// (bu yuzden yalniz birinci tekil/ucuncu sahis ticaret kaliplari bayi sayilir). Metinde olmayan hicbir sey uydurulmaz.
/// </summary>
public static class ProducerSignals
{
    private const int Strong = 3;
    private const int Medium = 2;
    private const int Weak = 1;

    /// <summary>Verdict icin gereken en dusuk puan.</summary>
    public const int Threshold = 3;

    private const int MaxQuotes = 3;
    private const int MaxQuoteLength = 200;

    private sealed record Rule(string Name, Regex Pattern, int Weight, Func<string, bool>? Context = null);

    private static Regex R(string pattern) =>
        new(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    // Desenler TurkishText.Normalize edilmis (kucuk harf, aksansiz) metin uzerinde calisir.
    private static readonly Rule[] ProducerRules =
    {
        // Somut olculer: sahte yapmasi zor, bayiler nadiren yazar.
        new("kapali alan m2", R(@"\d[\d.,]*\s*(?:m2|m²|m\^2|metrekare|metre kare)"), Strong,
            sentence => sentence.Contains("kapali") || sentence.Contains("uretim") || sentence.Contains("fabrika") || sentence.Contains("tesis")),
        new("uretim kapasitesi", R(@"uretim kapasite\w*|yillik\s+(?:uretim\s+)?[\d.,]+\s*(?:ton|adet|kg|metre|m3)|[\d.,]+\s*(?:ton|adet|kg)\s*/\s*(?:yil|ay|gun|saat)"), Strong),
        new("makine parki", R(@"makine park\w*|makine listesi|uretim hatt\w*"), Strong),
        new("kapasite raporu / sanayi sicil", R(@"kapasite raporu|sanayi sicil\w*"), Strong),

        // Orta: uretim anlatan birinci tekil/cogul cumleler.
        new("fabrikamiz / uretim tesisimiz", R(@"fabrikamiz\w*|kendi fabrika\w*|uretim tesisimiz\w*|uretim tesislerimiz\w*|uretim alanimiz\w*"), Medium),
        new("uretiyoruz", R(@"\buretiyoruz\b|\buretmekteyiz\b|imal ediyoruz|imal etmekteyiz|imalat yapiyoruz|imalat yapmaktayiz|uretim yapiyoruz|uretim yapmaktayiz|uretim yapmaktadir|imalatini yapiyoruz|uretimini yapiyoruz|uretimini yapmaktayiz"), Medium),
        new("fason uretim yapiyoruz", R(@"fason uretim yap\w*|fason imalat yap\w*|oem\b.{0,40}\buretim"), Medium),
    };

    private static readonly Rule[] TraderRules =
    {
        // Ticaret kaliplari: birinci tekil/cogul ya da "X'in ... bayisi".
        new("yetkili bayisi", R(@"yetkili bayisi\w*|yetkili saticisi\w*|resmi saticisi\w*|bayisiyiz|bayiyiz"), Strong),
        new("distribütör / ithalatçı", R(@"\bdistributoru\b|distributoruyuz|distributorlugu\w*|ithalatcisi\b|ithalatciyiz|ithal ediyoruz|ithal etmekteyiz|ithalatini yapiyoruz"), Strong),
        new("markalarımız", R(@"markalarimiz|temsil ettigimiz marka\w*|calistigimiz marka\w*"), Strong),
        new("fasona üretiriyoruz", R(@"urettiriyoruz|urettirmekteyiz|fason olarak urettiriyoruz"), Strong),

        // Zayif (tek basina yetmez): e-ticaret ve "temin ediyoruz" dili; uretici sitelerinde de gecebilir.
        new("sepete ekle", R(@"sepete ekle|sepetim|sepetiniz"), Weak),
        new("taksit / kargo", R(@"taksit\w*|ucretsiz kargo|kargo\w* bedava"), Weak),
        new("satın al", R(@"\bsatin al\b|hemen satin al|stokta var"), Weak),
        new("tedarik ediyoruz", R(@"tedarik ediyoruz|tedarik etmekteyiz|temin ediyoruz|temin etmekteyiz"), Weak),
        new("yetkili servis", R(@"yetkili servis\w*"), Weak),
    };

    /// <summary>Site metninden uretici/bayi kanitini cikarir. Bos metin = belirsiz.</summary>
    public static ProducerEvidence Analyze(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ProducerEvidence.None;

        var normalized = TurkishText.Normalize(text);

        var producer = Score(ProducerRules, text, normalized, out var producerQuotes);
        var trader = Score(TraderRules, text, normalized, out var traderQuotes);

        // Bayi karari kesindir (firma yapay zekaya sorulmadan elenir), bu yuzden yalniz HIC uretim kaniti yokken verilir:
        // "kendi fabrikamizda uretiyoruz" + "X'in yetkili bayisiyiz" diyen gercek bir uretici elenmesin.
        var kind = (producer, trader) switch
        {
            _ when producer >= Threshold && trader < Threshold => ProducerKind.Producer,
            _ when trader >= Threshold && producer == 0 => ProducerKind.Trader,
            _ => ProducerKind.Unclear   // iki taraf da var (marka sahibi + bayi agi), ya da kanit yok/zayif: yapay zeka karar verir
        };

        return new ProducerEvidence(kind, producer, trader, producerQuotes, traderQuotes);
    }

    /// <summary>
    /// Arama sonucunun basligi/ozeti siteyi okumadan bir bayi, ikinci el ya da kiralik ilani gibi mi duruyor?
    /// Yalnizca belirsiz olmayan kaliplar: "satın al", "fiyatları" marka sahibinin sitesinde de gecer, bu yuzden sayilmaz.
    /// Baslikta uretim kelimesi varsa ("... Üreticisi") karar verilmez.
    /// </summary>
    public static string? RetailListingHint(string? title, string? snippet)
    {
        var text = TurkishText.Normalize($"{title} {snippet}");
        if (text.Length == 0) return null;
        if (text.Contains("uretici") || text.Contains("imalat") || text.Contains("fabrika") || text.Contains("uretim")) return null;

        foreach (var (needle, label) in RetailHints)
            if (text.Contains(needle)) return label;
        return null;
    }

    private static readonly (string Needle, string Label)[] RetailHints =
    {
        // "yetkili saticilar" (marka sahibinin bayi bulma sayfasi) eslesmez; yalniz "X'in yetkili bayisi/saticisi" kalibi.
        ("yetkili bayisi", "yetkili bayi"),
        ("yetkili saticisi", "yetkili satıcı"),
        ("ikinci el", "ikinci el ilanı"),
        ("kiralik", "kiralık ilanı"),
        ("en ucuz", "fiyat karşılaştırma"),
    };

    private static int Score(Rule[] rules, string original, string normalized, out List<string> quotes)
    {
        quotes = new List<string>();
        var score = 0;

        foreach (var rule in rules)
        {
            // Her kural en fazla bir kez sayilir: tekrar eden menu/footer metni puani sisirmesin.
            string? quote = null;
            try
            {
                foreach (Match m in rule.Pattern.Matches(normalized))
                {
                    var (start, end) = SentenceAround(normalized, m.Index, m.Length);
                    if (rule.Context is not null && !rule.Context(normalized[start..end])) continue;
                    quote = Clip(original[start..end]);
                    break;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (quote is null) continue;
            score += rule.Weight;
            if (rule.Weight >= Medium && quotes.Count < MaxQuotes && !quotes.Contains(quote))
                quotes.Add(quote);
        }

        // Zayif kurallarin alintisi yalniz tek basina alinti gerektiginde (hic guclu yokken) eklenir.
        if (quotes.Count == 0 && score > 0)
        {
            foreach (var rule in rules.Where(r => r.Weight == Weak))
            {
                var m = rule.Pattern.Match(normalized);
                if (!m.Success) continue;
                var (s, e) = SentenceAround(normalized, m.Index, m.Length);
                quotes.Add(Clip(original[s..e]));
                break;
            }
        }

        return score;
    }

    /// <summary>
    /// Eslesmenin bulundugu cumleyi bulur. Normalize harf sayisini degistirmedigi icin dizinler orijinal metinde de gecerlidir.
    /// "15.000 m²" gibi sayilardaki nokta cumle sonu sayilmaz (noktadan sonra bosluk gerekir).
    /// </summary>
    private static (int Start, int End) SentenceAround(string text, int index, int length)
    {
        var start = index;
        while (start > 0 && !IsBoundary(text, start - 1)) start--;

        var end = index + length;
        while (end < text.Length && !IsBoundary(text, end)) end++;
        if (end < text.Length) end++;   // bitis isaretini de al

        // Cok uzun "cumleler" (menu metni) eslesme etrafinda kirpilir.
        if (end - start > MaxQuoteLength * 2)
        {
            start = Math.Max(start, index - MaxQuoteLength / 2);
            end = Math.Min(end, index + length + MaxQuoteLength / 2);
        }
        return (start, end);
    }

    private static bool IsBoundary(string text, int i)
    {
        var c = text[i];
        if (c is '\n' or '\r' or '|' or '•' or '·') return true;
        return c is '.' or '!' or '?' && (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]));
    }

    private static string Clip(string value)
    {
        var v = Regex.Replace(value, @"\s+", " ").Trim();
        return v.Length > MaxQuoteLength ? v[..MaxQuoteLength].TrimEnd() + "…" : v;
    }
}
