using System.Text.RegularExpressions;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Sayfa metninden "isim + unvan" ciftlerini cikarir.
/// AI #2 yerine kod tabanli yaklasim (dokuman bolum 25).
/// </summary>
public static partial class PersonExtractor
{
    // Turkce ve Ingilizce unvan anahtar kelimeleri.
    private static readonly string[] TitleKeywords =
    {
        "genel müdür", "genel müdür yardımcısı", "fabrika müdürü", "üretim müdürü",
        "bilgi işlem müdürü", "bilgi teknolojileri müdürü", "bilgi işlem sorumlusu",
        "it müdürü", "it manager", "it director", "cio", "cto", "ceo",
        "satın alma müdürü", "kalite müdürü", "planlama müdürü",
        "sap danışmanı", "sap uzmanı", "production manager", "plant manager",
        "yönetim kurulu başkanı", "genel koordinatör", "operasyon müdürü"
    };

    // Iki-dort kelimelik, her kelimesi buyuk harfle baslayan Turkce isim kalibi.
    [GeneratedRegex(@"\b[A-ZÇĞİÖŞÜ][a-zçğıöşü]{1,20}(?:\s+[A-ZÇĞİÖŞÜ][a-zçğıöşü]{1,20}){1,2}\b", RegexOptions.Compiled)]
    private static partial Regex NameRegex();

    /// <summary>
    /// Unvan gecen satirlarin cevresinde isim arar.
    /// Isim bulunamazsa kisi yine dondurulur (unvan tek basina da degerlidir).
    /// </summary>
    public static List<(string Name, string Title)> Extract(string text)
    {
        var results = new List<(string, string)>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length > 120) continue; // uzun paragraf, muhtemelen metin govdesi

            if (LooksLikeMenu(line)) continue;

            // En uzun eslesen unvan: "Genel Müdür Yardımcısı" satirinda "genel müdür" degil tamami alinir.
            var matchedTitle = TitleKeywords.Where(k => TurkishText.ContainsWord(line, k)).OrderByDescending(k => k.Length).FirstOrDefault();
            if (matchedTitle is null) continue;

            // Isim ayni satirda, bir onceki veya bir sonraki satirda olabilir.
            var candidates = new List<string> { line };
            if (i > 0) candidates.Add(lines[i - 1].Trim());
            if (i < lines.Length - 1) candidates.Add(lines[i + 1].Trim());

            string? name = null;
            foreach (var candidate in candidates)
            {
                if (candidate.Length > 80) continue;
                foreach (Match m in NameRegex().Matches(candidate))
                {
                    // Isimden sonra gelen buyuk harfli kelime ("Gökhan Sığın Named ...") isme katilmaz:
                    // bastan isim olabilecek en uzun kelime dizisi alinir, en az iki kelime olmali.
                    var words = m.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .TakeWhile(w => IsPlausibleName(w)).ToList();
                    if (words.Count < 2) continue;
                    var value = string.Join(' ', words);
                    // Unvanin kendisini ya da sitedeki dugme/baslik metnini ("Sosyal Medyadan Ulaşın") isim sanmayalim.
                    if (TitleKeywords.Any(k => TurkishText.ContainsWord(value, k))) continue;
                    name = value;
                    break;
                }
                if (name is not null) break;
            }

            // Isimsiz kayit satis icin kullanilamaz ve menu gurultusunun buyuk kismi
            // isimsiz geldigi icin burada eleniyor.
            if (name is null) continue;

            results.Add((name, CleanTitle(line, matchedTitle, name)));
        }

        return results
            .GroupBy(r => (r.Item1 + "|" + r.Item2).ToLowerInvariant())
            .Select(g => g.First())
            .Take(20)
            .ToList();
    }

    /// <summary>
    /// Menu bloklari duz metne cevrildiginde tek satirda bitisik baslıklar olarak gelir
    /// ("HakkımızdaTarihçeKurucu..."). Bunlar kisi degil, elenmeli.
    /// </summary>
    private static bool LooksLikeMenu(string line)
    {
        // Kelime arasi bosluk olmadan buyuk harfe gecisler menu birlesmesinin isareti.
        var glued = 0;
        for (var i = 1; i < line.Length; i++)
            if (char.IsLower(line[i - 1]) && char.IsUpper(line[i]))
                glued++;

        return glued >= 3;
    }

    /// <summary>
    /// Unvan satirini kisaltir. Uzun satirlarda eslesen anahtar kelimenin cevresini alir,
    /// boylece Contact.Title alanina paragraf degil unvan yazilir.
    /// </summary>
    private static string CleanTitle(string line, string matchedKeyword, string? name = null)
    {
        // Kisa ve cumle olmayan satir tam unvandir; kisinin adi ayni satirdaysa ayiklanir
        // ("Ahmet Yılmaz - Bilgi İşlem Müdürü" → "Bilgi İşlem Müdürü").
        if (line.Length <= 60 && !LooksLikeSentence(line))
        {
            var title = line;
            if (!string.IsNullOrEmpty(name))
            {
                var at = title.IndexOf(name, StringComparison.Ordinal);
                if (at >= 0) title = title.Remove(at, name.Length);
            }
            title = title.Trim(' ', '-', '–', '—', '|', ':', ',', '/', '·', '\t');
            return title.Length >= 2 ? title : matchedKeyword;
        }

        // Cumle ya da uzun satir: unvan alanina yalnizca unvanin kendisi yazilir (yarim kelimeyle kesilmis cumle degil).
        // Normalize karakter sayisini degistirmedigi icin indeks ham metinde de gecerli.
        var index = TurkishText.Normalize(line)
            .IndexOf(TurkishText.Normalize(matchedKeyword), StringComparison.Ordinal);
        return index < 0 ? matchedKeyword : line.Substring(index, matchedKeyword.Length).Trim();
    }

    // Cumle isaretleri: ünvan satiri degil, anlatim ("... olarak görev yapıyor", "Halen ...").
    private static readonly string[] SentenceWords =
        { "olarak", "halen", "görev", "yapıyor", "yapmakta", "sürdürüyor", "beri", "itibaren", "tarafından", "sonra", "önce",
          "named", "appointed", "joins", "joined", "announces", "welcomes", "promoted", "atandı", "getirildi", "katıldı" };

    private static bool LooksLikeSentence(string line) =>
        SentenceWords.Any(w => TurkishText.ContainsWord(line, w)) || line.TrimEnd().EndsWith('.');

    // Isim olamayacak kelimeler: site dugmeleri/basliklari, kurum ve yer kelimeleri, unvan parcalari.
    private static readonly HashSet<string> NonNameWords = new(new[]
    {
        "sosyal", "medya", "medyadan", "ulasin", "ulasmak", "bize", "iletisim", "iletisime", "gecin", "takip", "edin", "tiklayin",
        "devami", "detay", "detaylar", "incele", "hakkimizda", "kurumsal", "haberler", "duyurular", "urunler", "urunlerimiz",
        "hizmetler", "hizmetlerimiz", "projeler", "projelerimiz", "referanslar", "referanslarimiz", "kariyer", "insan", "kaynaklari",
        "misyon", "vizyon", "degerlerimiz", "kalite", "politikasi", "politikamiz", "belgeler", "sertifikalar", "galeri", "katalog",
        "teklif", "online", "destek", "musteri", "adres", "telefon", "faks", "posta", "whatsapp", "instagram", "facebook",
        "linkedin", "twitter", "youtube", "anasayfa", "ana", "sayfa", "blog", "mesaj", "mesaji", "yonetim", "kurulu", "baskani",
        "baskan", "mudur", "muduru", "mudurlugu", "genel", "sorumlu", "sorumlusu", "uzman", "uzmani", "ekibimiz", "kadromuz",
        "halen", "sayin", "degerli", "sirket", "sirketi", "sirketimiz", "firma", "firmamiz", "grup", "grubu", "holding", "sanayi",
        "ticaret", "anonim", "limited", "fabrika", "fabrikasi", "tesis", "tesisleri", "merkez", "merkezi", "subesi", "bolge",
        "turkiye", "dunya", "global", "uluslararasi", "yurt", "ici", "disi", "cozum", "cozumler", "teknoloji", "teknolojileri",
        "bilgi", "islem", "satin", "alma", "uretim", "planlama", "operasyon", "koordinator", "danisman", "danismani",
        "named", "new", "appointed", "joins", "joined", "announces", "welcomes", "promoted", "the", "of", "and", "at"
    });

    /// <summary>Kisi adi gibi mi: kelimelerden hicbiri site/kurum/unvan kelimesi olmamali.</summary>
    public static bool IsPlausibleName(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => !NonNameWords.Contains(TurkishText.Normalize(w)));
}
