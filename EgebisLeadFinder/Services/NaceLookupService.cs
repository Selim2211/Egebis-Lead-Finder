using Microsoft.Extensions.Caching.Memory;

namespace EgebisLeadFinder.Services;

/// <summary>Bir NACE kodunun cozumlenmis hali (kutudaki bilgi baloncugu ve arama terimleri icin).</summary>
/// <param name="Name">Kod adi. Resmi listeden geliyorsa (6 haneli kod, 2 haneli bolum) <paramref name="Official"/> true.</param>
/// <param name="ShortName">Sektor kutusunu doldurmak icin 2-4 kelimelik ad.</param>
/// <param name="Official">Ad resmi TUIK/GIB listesinden mi (yoksa resmi alt faaliyetlerden yapay zekanin ozeti mi)?</param>
/// <param name="Basis">Grup kodlari (27.51 gibi) icin yapay zekanin dayandigi resmi alt faaliyetlerden kisa bir ornek.</param>
/// <param name="Products">Kodun resmi metninde sayilan urunler (resmi metinde gectigi dogrulanmis). NACE aramasi bu urunlerden
/// en az birini URETEN firmalari arar.</param>
public record NaceInfo(
    string Code, string? DivisionName, string? SectionName, string? Name, string? ShortName, string? Description,
    IReadOnlyList<string> SearchTerms, bool Official, string? Basis, string? Error)
{
    public bool Ok => Error is null;
    public IReadOnlyList<string> Products { get; init; } = Array.Empty<string>();
}

/// <summary>NACE kodunun urun listesi: resmi metne dayali dogrulama, yedek ayirma ve kullanicinin secimi.</summary>
public static class NaceProducts
{
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "ev", "tipi", "vb", "vs", "ve", "ile", "icin", "dahil", "haric", "olan", "olanlar", "imalati", "uretimi", "urunleri",
        "diger", "benzeri", "turu", "turlu", "cesitli"
    };

    /// <summary>
    /// Urun adinin anlamli her kelimesi (ilk 5 harfi) resmi metinde geciyor mu? Yapay zekanin metinde olmayan bir urun
    /// eklemesini engeller ("derin dondurucu" resmi metinde yalnizca "dondurucu" ise reddedilir).
    /// </summary>
    public static bool IsGrounded(string product, string officialText)
    {
        var text = TurkishText.Normalize(officialText);
        var words = Words(product);
        return words.Count > 0 && words.All(w => text.Contains(w.Length > 5 ? w[..5] : w, StringComparison.Ordinal));
    }

    /// <summary>
    /// Yedek: resmi adi virgul ve "ve" ile parcalara ayirir ("Ev tipi buzdolabı, dondurucu, … vb. imalatı" -> "buzdolabı",
    /// "dondurucu", …). Yapay zeka cevap veremezse kullanilir.
    /// </summary>
    public static List<string> Split(string? officialName)
    {
        if (string.IsNullOrWhiteSpace(officialName)) return new();
        var text = System.Text.RegularExpressions.Regex.Replace(officialName, @"\([^)]*\)", " ");
        return text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(p => System.Text.RegularExpressions.Regex.Split(p, @"\s+ve\s+|\s+ile\s+"))
            .Select(Clean)
            .Where(p => p.Length is > 2 and <= 60)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToList();
    }

    /// <summary>
    /// Kullanicinin sectigi urunler (formdan virgullu) ile izin verilen liste kesisimi; secim yoksa ya da hicbiri
    /// gecerli degilse tum liste. Liste disi urun aramaya giremez.
    /// </summary>
    public static List<string> Resolve(string? selectedCsv, IReadOnlyList<string> allowed)
    {
        var map = allowed.GroupBy(a => TurkishText.Normalize(a)).ToDictionary(g => g.Key, g => g.First());
        var chosen = (selectedCsv ?? string.Empty)
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => map.GetValueOrDefault(TurkishText.Normalize(s)))
            .OfType<string>()
            .Distinct()
            .ToList();
        return chosen.Count > 0 ? chosen : allowed.ToList();
    }

    private static string Clean(string part)
    {
        var p = System.Text.RegularExpressions.Regex.Replace(part, @"\b(vb|vs)\.?", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        p = System.Text.RegularExpressions.Regex.Replace(p, @"\b(imalatı|üretimi|imalatı\.?)\s*$", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        p = System.Text.RegularExpressions.Regex.Replace(p, @"^\s*ev tipi\s+", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        p = System.Text.RegularExpressions.Regex.Replace(p, @"\s+", " ").Trim().Trim('.', ' ');
        return p.Length > 0 ? char.ToLower(p[0], new System.Globalization.CultureInfo("tr-TR")) + p[1..] : p;
    }

    private static List<string> Words(string product) =>
        TurkishText.Normalize(product)
            .Split(new[] { ' ', '-', '/', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !Filler.Contains(w))
            .ToList();
}

/// <summary>
/// Kullanicinin yazdigi NACE kodunu (ör. 27.51.08) cozer. Kodun gecerliligi ve adi RESMI listeden
/// (TUIK/GIB NACE Rev.2 Altili, 2.201 kod) gelir: yapay zeka kodun anlamini hafizasindan UYDURMASIN diye
/// ona yalnizca resmi ad / resmi alt faaliyetler verilir ve sadece aciklama + arama terimi urettirilir.
/// Ayni kod 30 gun onbellekte tutulur.
/// </summary>
public class NaceLookupService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromDays(30);
    private const int MaxContextItems = 14;
    private readonly IInsightAi _ai;
    private readonly IMemoryCache _cache;
    private readonly ILogger<NaceLookupService> _logger;

    public NaceLookupService(IInsightAi ai, IMemoryCache cache, ILogger<NaceLookupService> logger)
    {
        _ai = ai;
        _cache = cache;
        _logger = logger;
    }

    public async Task<NaceInfo> LookupAsync(string? rawCode, CancellationToken ct = default)
    {
        var code = NaceCatalog.NormalizeFull(rawCode);
        if (code is null)
            return Fail(rawCode?.Trim() ?? string.Empty, "Geçersiz NACE kodu. 2, 4 veya 6 haneli girin (örn. 27, 27.51 ya da 27.51.08).");

        var division = NaceCatalog.Division(code);
        if (division is null)
            return Fail(code, $"“{code[..2]}” diye bir NACE bölümü yok.");

        var cacheKey = "nace-info:" + code;
        if (_cache.TryGetValue(cacheKey, out NaceInfo? cached) && cached is not null) return cached;

        var digits = code.Replace(".", string.Empty);
        NaceInfo info;
        if (digits.Length == 6)
        {
            var official = NaceCatalog.Activity(digits);
            if (official is null)
                return Fail(code, $"{code} resmî NACE Rev.2 listesinde yok. Kodu kontrol edin (örn. {SuggestSibling(digits)}).");

            var ai = await DescribeAsync(code, $"Resmî faaliyet adı (TÜİK/GİB NACE Rev.2 Altılı): {official}", ct);
            info = Build(code, division, official, ai, official: true, basis: null) with { Products = ProductsFrom(ai, official, NaceProducts.Split(official)) };
        }
        else if (digits.Length == 2)
        {
            var ai = await DescribeAsync(code, $"Resmî bölüm adı (NACE Rev.2): {division.Name} (kısım {division.SectionCode}: {division.SectionName})", ct);
            info = Build(code, division, division.Name, ai, official: true, basis: null);
            // Bolumun resmi urun listesi yok: yapay zekanin (bolum adina dayali) urunleri kullanilir.
            info = info with { Products = ai.Error is null && ai.Products.Count > 0 ? ai.Products.Take(10).ToList() : info.SearchTerms };
        }
        else
        {
            // 3-4 haneli grup/sinif: resmi 6 haneli alt faaliyetler listelenir; yapay zeka yalnizca bunlara dayanir.
            var subs = NaceCatalog.ActivitiesUnder(digits);
            if (subs.Count == 0)
                return Fail(code, $"{code} ile başlayan resmî bir faaliyet kodu yok. Kodu kontrol edin.");

            var lines = subs.Take(MaxContextItems).Select(s => $"- {NaceCatalog.FormatSixDigit(s.Key)}: {s.Value}");
            var ai = await DescribeAsync(code, $"Bu kodun kapsadığı resmî alt faaliyetler (TÜİK/GİB NACE Rev.2 Altılı, {subs.Count} adet):\n{string.Join('\n', lines)}", ct);
            var basis = string.Join("; ", subs.Take(3).Select(s => Short(s.Value, 70)));
            info = Build(code, division, null, ai, official: false, basis: basis + (subs.Count > 3 ? $" … (+{subs.Count - 3})" : string.Empty));
            var subsText = string.Join(" ", subs.Select(s => s.Value));
            info = info with { Products = ProductsFrom(ai, subsText, subs.SelectMany(s => NaceProducts.Split(s.Value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList()) };
            if (info.Name is null)
                info = info with { Name = $"{code} sınıfı ({subs.Count} resmî alt faaliyet)", ShortName = division.Name };
        }

        _cache.Set(cacheKey, info, CacheFor);
        return info;
    }

    /// <summary>Yapay zekanin urunlerinden resmi metinde gercekten gecenler; hic kalmazsa resmi metnin ayrilmis hali.</summary>
    private static List<string> ProductsFrom(NaceDescribeResult ai, string officialText, List<string> fallback)
    {
        var grounded = ai.Error is null
            ? ai.Products.Where(p => NaceProducts.IsGrounded(p, officialText)).Take(15).ToList()
            : new List<string>();
        return grounded.Count > 0 ? grounded : fallback;
    }

    private async Task<NaceDescribeResult> DescribeAsync(string code, string context, CancellationToken ct)
    {
        try
        {
            return await _ai.DescribeNaceAsync(code, context, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "NACE kodu açıklaması alınamadı: {Code}", code);
            return new NaceDescribeResult { Error = "Yapay zekâya ulaşılamadı." };
        }
    }

    /// <summary>
    /// Ad resmi listeden (varsa) alinir; yapay zeka basarisizsa bile kod cozulur: terimler resmi adin
    /// ilk kisminden (parantezden once) uretilir, aciklama bos kalir.
    /// </summary>
    private static NaceInfo Build(string code, NaceDivision division, string? officialName, NaceDescribeResult ai, bool official, string? basis)
    {
        var aiOk = ai.Error is null;
        var name = officialName ?? (aiOk ? ai.Name : null);
        var shortName = aiOk && !string.IsNullOrWhiteSpace(ai.ShortName) ? ai.ShortName! : Short(StemOf(officialName ?? name), 60);
        IReadOnlyList<string> terms = aiOk && ai.SearchTerms.Count > 0
            ? ai.SearchTerms
            : string.IsNullOrWhiteSpace(StemOf(officialName)) ? Array.Empty<string>() : new[] { Short(StemOf(officialName), 60) };

        return new NaceInfo(code, division.Name, division.SectionName, name, string.IsNullOrWhiteSpace(shortName) ? null : shortName,
            aiOk ? ai.Description : null, terms, official, basis, null);
    }

    /// <summary>"Ev tipi buzdolabı, dondurucu, … (…) vb. imalatı" -> "Ev tipi buzdolabı" (listenin ilk parcasi).</summary>
    private static string StemOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var cut = name.IndexOfAny(new[] { '(', ',' });
        return (cut > 3 ? name[..cut] : name).Trim();
    }

    private static string Short(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";

    /// <summary>Hata mesajinda ornek: ayni 4 haneli sinifin ilk resmi kodu.</summary>
    private static string SuggestSibling(string digits)
    {
        var sibling = NaceCatalog.ActivitiesUnder(digits[..4]).FirstOrDefault().Key;
        return sibling is null ? "27.51.08" : NaceCatalog.FormatSixDigit(sibling);
    }

    private static NaceInfo Fail(string code, string error) =>
        new(code, null, null, null, null, null, Array.Empty<string>(), false, null, error);
}
