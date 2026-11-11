using System.Text.RegularExpressions;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Ayarlar'daki Black List: aramada bulunsa dahi gosterilmeyecek firmalar. Her satir (veya virgulle ayrilan
/// oge) bir alan adi ("ornek.com") ya da firma adidir ("Örnek Makina"). Alan adlari alt alan adlarini da kapsar;
/// firma adlari buyuk/kucuk harf ve Turkce harf farki gozetmeden, tam kelime olarak eslesir ("Ege" "Egebis"i kapsamaz).
/// </summary>
public sealed partial class SearchBlacklist
{
    public static readonly SearchBlacklist Empty = new(new List<string>(), new List<string>());

    public IReadOnlyList<string> Domains { get; }

    /// <summary>Normallestirilmis firma adlari.</summary>
    public IReadOnlyList<string> Names { get; }

    private SearchBlacklist(List<string> domains, List<string> names)
    {
        Domains = domains;
        Names = names;
    }

    public static SearchBlacklist Parse(string? text)
    {
        var domains = new List<string>();
        var names = new List<string>();

        foreach (var raw in (text ?? string.Empty).Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var domain = AsDomain(raw);
            if (domain is not null)
            {
                if (!domains.Contains(domain, StringComparer.OrdinalIgnoreCase)) domains.Add(domain);
                continue;
            }

            var name = TurkishText.Normalize(raw).Trim();
            if (name.Length >= MinNameLength && !names.Contains(name)) names.Add(name);
        }

        return new SearchBlacklist(domains, names);
    }

    /// <summary>Tek harfli gibi cok kisa adlar butun sonuclari elemesin.</summary>
    private const int MinNameLength = 2;

    /// <summary>Bosluk icermeyen ve uzantisi olan oge alan adidir; adres yazildiysa alan adi cikarilir.</summary>
    private static string? AsDomain(string value)
    {
        var v = value.Trim().ToLowerInvariant().TrimStart('*', '.');
        v = ProtocolPrefix().Replace(v, string.Empty);
        if (v.StartsWith("www.", StringComparison.Ordinal)) v = v[4..];
        var slash = v.IndexOf('/');
        if (slash >= 0) v = v[..slash];
        return DomainShape().IsMatch(v) ? v : null;
    }

    public bool IsEmpty => Domains.Count == 0 && Names.Count == 0;

    public bool MatchesDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        var d = domain.Trim().ToLowerInvariant();
        if (d.StartsWith("www.", StringComparison.Ordinal)) d = d[4..];
        if (Domains.Any(b => d == b || d.EndsWith("." + b, StringComparison.Ordinal))) return true;

        // Tek kelimelik ad ("Tolon") alan adinin ana etiketiyle ayniysa (tolon.com.tr) o site de engellenir.
        var label = TurkishText.Normalize(d.Split('.')[0].Replace("-", string.Empty));
        return Names.Any(n => n.IndexOf(' ') < 0 && n == label);
    }

    public bool MatchesName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || Names.Count == 0) return false;
        var normalized = TurkishText.Normalize(text);
        return Names.Any(n => ContainsWhole(normalized, n));
    }

    public bool Matches(string? domain, params string?[] names) => MatchesDomain(domain) || names.Any(MatchesName);

    private static bool ContainsWhole(string text, string name)
    {
        for (var i = text.IndexOf(name, StringComparison.Ordinal); i >= 0; i = text.IndexOf(name, i + 1, StringComparison.Ordinal))
        {
            var startOk = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            var end = i + name.Length;
            var endOk = end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (startOk && endOk) return true;
        }
        return false;
    }

    [GeneratedRegex(@"^[a-z]+://")]
    private static partial Regex ProtocolPrefix();

    [GeneratedRegex(@"^[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}$")]
    private static partial Regex DomainShape();
}
