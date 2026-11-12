using EgebisLeadFinder.Controllers;

namespace EgebisLeadFinder.Models;

/// <summary>"Sirketimiz" formu: listeler virgullu metin olarak duzenlenir.</summary>
public class BusinessProfileForm
{
    public string? CompanyName { get; set; }
    public string? Website { get; set; }
    public string? Offering { get; set; }
    public string? ProblemsWeSolve { get; set; }
    public string? IdealCustomer { get; set; }
    public string? NotCustomers { get; set; }
    public string? Competitors { get; set; }
    public string? ExampleCustomers { get; set; }

    /// <summary>"", "manufacturer" veya "any" (bkz. BusinessProfile.CustomerKind).</summary>
    public string? CustomerKind { get; set; }

    public string? BuyingSignals { get; set; }

    /// <summary>Puanlama anahtar kelimeleri; satır başına "Ad | kelime, kelime | puan".</summary>
    public string? ScoringSignals { get; set; }

    /// <summary>Ayarlar'daki lead unvanlari (Apollo ve site aramasinda kullanilir).</summary>
    public string? TargetTitles { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsConfigured { get; set; }

    public const string DefaultExampleCustomers = "Vestel, Simfer, Polat Makina";

    public static BusinessProfileForm From(BusinessProfile p, IEnumerable<string> titles) => new()
    {
        CompanyName = p.CompanyName,
        Website = p.Website,
        Offering = p.Offering,
        ProblemsWeSolve = p.ProblemsWeSolve,
        IdealCustomer = p.IdealCustomer,
        NotCustomers = p.NotCustomers,
        Competitors = p.Competitors,
        // Hic kaydedilmemis profilde ornek musteri alani bos kalmasin; kullanici duzeltip kaydeder.
        ExampleCustomers = p.ExampleCustomers.Count == 0 && p.UpdatedAt is null
            ? DefaultExampleCustomers
            : string.Join(", ", p.ExampleCustomers),
        CustomerKind = p.CustomerKind,
        BuyingSignals = string.Join(", ", p.BuyingSignals),
        ScoringSignals = ScoringSignal.FormatLines(p.ScoringSignals),
        TargetTitles = string.Join(", ", titles),
        UpdatedAt = p.UpdatedAt,
        UpdatedBy = p.UpdatedBy,
        IsConfigured = p.IsConfigured
    };

    public BusinessProfile ToProfile() => new()
    {
        CompanyName = CompanyName ?? string.Empty,
        Website = Website,
        Offering = Offering ?? string.Empty,
        ProblemsWeSolve = ProblemsWeSolve,
        IdealCustomer = IdealCustomer ?? string.Empty,
        NotCustomers = NotCustomers,
        Competitors = Competitors,
        ExampleCustomers = IcpController.SplitList(ExampleCustomers),
        CustomerKind = CustomerKind,
        BuyingSignals = IcpController.SplitList(BuyingSignals),
        ScoringSignals = ScoringSignal.ParseLines(ScoringSignals)
    };
}
