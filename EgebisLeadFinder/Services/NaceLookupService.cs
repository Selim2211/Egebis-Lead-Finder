using Microsoft.Extensions.Caching.Memory;

namespace EgebisLeadFinder.Services;

/// <summary>Bir NACE kodunun cozumlenmis hali (kutudaki bilgi baloncugu ve arama terimleri icin).</summary>
/// <param name="Name">Kod adi. Resmi listeden geliyorsa (6 haneli kod, 2 haneli bolum) <paramref name="Official"/> true.</param>
/// <param name="ShortName">Sektor kutusunu doldurmak icin 2-4 kelimelik ad.</param>
/// <param name="Official">Ad resmi TUIK/GIB listesinden mi (yoksa resmi alt faaliyetlerden yapay zekanin ozeti mi)?</param>
/// <param name="Basis">Grup kodlari (27.51 gibi) icin yapay zekanin dayandigi resmi alt faaliyetlerden kisa bir ornek.</param>
public record NaceInfo(
    string Code, string? DivisionName, string? SectionName, string? Name, string? ShortName, string? Description,
    IReadOnlyList<string> SearchTerms, bool Official, string? Basis, string? Error)
{
    public bool Ok => Error is null;
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
            info = Build(code, division, official, ai, official: true, basis: null);
        }
        else if (digits.Length == 2)
        {
            var ai = await DescribeAsync(code, $"Resmî bölüm adı (NACE Rev.2): {division.Name} (kısım {division.SectionCode}: {division.SectionName})", ct);
            info = Build(code, division, division.Name, ai, official: true, basis: null);
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
            if (info.Name is null)
                info = info with { Name = $"{code} sınıfı ({subs.Count} resmî alt faaliyet)", ShortName = division.Name };
        }

        _cache.Set(cacheKey, info, CacheFor);
        return info;
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
