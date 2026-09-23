using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services.CompanyIntel;

/// <summary>
/// Firma on arastirmasi icin tek bir kaynak (haber, KAP, resmi kayit linkleri...).
/// Her kaynak arayuz arkasindadir ve hata durumunda bos donerek akisi kirmaz
/// (Apollo -> LinkedIn kademeli dusus felsefesi).
/// </summary>
public interface ICompanyIntelSource
{
    /// <summary>Kaynagin okunabilir adi ("Haber", "KAP", "Resmi Kayitlar").</summary>
    string Name { get; }

    Task<SourceIntel> CollectAsync(Company company, CancellationToken ct = default);
}

/// <summary>Bir kaynagin firma hakkinda topladigi ham parcalar + "kendin ac" linkleri.</summary>
public class SourceIntel
{
    public string SourceName { get; init; } = string.Empty;

    /// <summary>AI'a verilecek ham metin parcalari (haber snippet'i, bildirim basligi...).</summary>
    public List<IntelSnippet> Snippets { get; init; } = new();

    /// <summary>Kullanicinin elle acabilecegi kaynak linkleri (MERSIS, Ticaret Sicil...).</summary>
    public List<IntelLink> Links { get; init; } = new();

    /// <summary>Kaynak calisti ama hicbir sey bulamadi mi? Hata degildir.</summary>
    public bool FoundSomething => Snippets.Count > 0;

    /// <summary>Kaynak erisilemedi/patladi ise nedeni. Null ise sorun yok.</summary>
    public string? Error { get; set; }

    public static SourceIntel Empty(string source) => new() { SourceName = source };
}

/// <summary>Kaynaktan gelen tek bir metin parcasi.</summary>
public class IntelSnippet
{
    public string Text { get; init; } = string.Empty;
    public string? SourceUrl { get; init; }
    public IntelKind Kind { get; init; } = IntelKind.Genel;
}

/// <summary>"Kendin ac" turu kaynak linki.</summary>
public class IntelLink
{
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public enum IntelKind
{
    Genel = 0,
    Haber = 1,
    Finansal = 2,
    Risk = 3,
    Buyume = 4,
    Kayit = 5
}
