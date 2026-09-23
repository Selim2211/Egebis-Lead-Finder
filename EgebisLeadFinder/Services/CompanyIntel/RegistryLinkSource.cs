using System.Net;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services.CompanyIntel;

/// <summary>
/// Resmi kaynaklar (Ticaret Sicil Gazetesi, MERSIS, EKAP, Findeks) CAPTCHA / e-Devlet
/// girisi / taranmis PDF nedeniyle otomatik okunamaz. Bu kaynak veri cekmez;
/// kullanicinin tek tikla acabilecegi hazir arama linklerini uretir.
/// </summary>
public class RegistryLinkSource : ICompanyIntelSource
{
    private readonly ResearchOptions _options;

    public RegistryLinkSource(IOptions<ResearchOptions> options) => _options = options.Value;

    public string Name => "Resmi kayıtlar (elle kontrol)";

    public Task<SourceIntel> CollectAsync(Company company, CancellationToken ct = default)
    {
        var intel = new SourceIntel { SourceName = Name };
        var name = (company.Name ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            intel.Error = "Firma adı yok.";
            return Task.FromResult(intel);
        }

        var q = WebUtility.UrlEncode(name);

        AddLink(intel, "Ticaret Sicil Gazetesi", _options.TicaretSicilUrlTemplate, q);
        AddLink(intel, "MERSİS", _options.MersisUrl, q);
        AddLink(intel, "EKAP (kamu ihaleleri)", _options.EkapSearchUrlTemplate, q);
        AddLink(intel, "Findeks Ticari Rapor (ücretli)", _options.FindeksUrl, q);

        return Task.FromResult(intel);
    }

    private static void AddLink(SourceIntel intel, string label, string template, string encodedQuery)
    {
        if (string.IsNullOrWhiteSpace(template)) return;

        var url = template.Contains("{q}", StringComparison.Ordinal)
            ? template.Replace("{q}", encodedQuery)
            : template;

        intel.Links.Add(new IntelLink { Label = label, Url = url });
    }
}
