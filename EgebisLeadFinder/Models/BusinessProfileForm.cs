using EgebisLeadFinder.Controllers;

namespace EgebisLeadFinder.Models;

/// <summary>"Biz ne arıyoruz?" formu: listeler virgullu metin olarak duzenlenir.</summary>
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

    /// <summary>Ayarlar'daki lead unvanlari (Apollo ve site aramasinda kullanilir).</summary>
    public string? TargetTitles { get; set; }

    public List<SegmentForm> Segments { get; set; } = new();

    /// <summary>Kaydederken segmentlerin NACE/kelime/eleme listeleri ICP'ye eklensin mi?</summary>
    public bool ApplyToIcp { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsConfigured { get; set; }

    public static BusinessProfileForm From(BusinessProfile p, IEnumerable<string> titles) => new()
    {
        CompanyName = p.CompanyName,
        Website = p.Website,
        Offering = p.Offering,
        ProblemsWeSolve = p.ProblemsWeSolve,
        IdealCustomer = p.IdealCustomer,
        NotCustomers = p.NotCustomers,
        Competitors = p.Competitors,
        ExampleCustomers = string.Join(", ", p.ExampleCustomers),
        CustomerKind = p.CustomerKind,
        BuyingSignals = string.Join(", ", p.BuyingSignals),
        TargetTitles = string.Join(", ", titles),
        Segments = p.Segments.Select(s => new SegmentForm
        {
            Id = s.Id,
            Name = s.Name,
            Description = s.Description,
            SearchTerm = s.SearchTerm,
            RegionKey = s.RegionKey,
            Keywords = string.Join(", ", s.Keywords),
            NaceCodes = string.Join(", ", s.NaceCodes),
            ExcludeKeywords = string.Join(", ", s.ExcludeKeywords),
            TargetTitles = string.Join(", ", s.TargetTitles)
        }).ToList(),
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
        Segments = Segments.Select(s => new TargetSegment
        {
            Id = s.Id ?? string.Empty,
            Name = s.Name ?? string.Empty,
            Description = s.Description,
            SearchTerm = s.SearchTerm,
            RegionKey = s.RegionKey,
            Keywords = IcpController.SplitList(s.Keywords),
            NaceCodes = IcpController.SplitList(s.NaceCodes),
            ExcludeKeywords = IcpController.SplitList(s.ExcludeKeywords),
            TargetTitles = IcpController.SplitList(s.TargetTitles)
        }).ToList()
    };
}

public class SegmentForm
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? SearchTerm { get; set; }
    public string? RegionKey { get; set; }
    public string? Keywords { get; set; }
    public string? NaceCodes { get; set; }
    public string? ExcludeKeywords { get; set; }
    public string? TargetTitles { get; set; }
}
