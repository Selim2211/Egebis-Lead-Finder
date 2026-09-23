using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services.CompanyIntel;

/// <summary>
/// On arastirma icin hedefli arama sorgularini kod tarafinda uretir.
/// Sorgular AI'a sorulmaz; sabit sablonlar hem ucuz hem tahmin edilebilir
/// (SearchQueryBuilder ile ayni felsefe).
/// </summary>
public static class CompanyResearchQueryBuilder
{
    /// <summary>Firma icin haber/itibar/risk/buyume sorgularini uretir.</summary>
    public static List<string> Build(Company company, ResearchOptions options)
    {
        var name = (company.Name ?? string.Empty).Trim();
        if (name.Length == 0) return new List<string>();

        var q = $"\"{name}\"";

        var queries = new List<string>
        {
            // Buyume / yatirim / ticari aktivite
            $"{q} (yatırım OR fabrika OR kapasite OR ihale OR anlaşma OR ihracat)",
            // Risk / hukuki sorun
            $"{q} (konkordato OR iflas OR haciz OR icra OR \"ödeme güçlüğü\" OR tasfiye OR kapandı)",
            // Finansal
            $"{q} (ciro OR bilanço OR sermaye OR \"gelir tablosu\" OR hasılat)",
            // Ortaklik / yonetim
            $"{q} (\"yönetim kurulu\" OR \"genel müdür\" OR ortak OR \"satın aldı\" OR birleşti)",
            // Musteri / tedarikci / referans
            $"{q} (müşteri OR tedarikçi OR referans OR \"iş ortağı\")",
            // Karar vericiler
            $"{q} (\"genel müdür\" OR CEO OR CFO OR \"bilgi işlem müdürü\" OR \"IT müdürü\" OR CIO OR \"yönetim kurulu başkanı\")",
            // Teknoloji / IT altyapisi
            $"{q} (SAP OR ERP OR \"dijital dönüşüm\" OR Logo OR Netsis OR \"Microsoft Dynamics\" OR Oracle OR MES)",
            // IT is ilanlari: yatirim/ihtiyac sinyali
            $"{q} (kariyer.net OR \"iş ilanı\" OR linkedin.com/jobs) (SAP OR ERP OR yazılım OR \"bilgi işlem\")",
            // Buyukluk
            $"{q} (\"çalışan sayısı\" OR personel OR ihracat OR \"üretim kapasitesi\" OR metrekare)",
            // Grup sirketleri
            $"{q} (\"grup şirketi\" OR holding OR iştirak OR \"bağlı ortaklık\")",
            // Genel guncel haberler
            $"{q} haber",
            // Resmi kayit izleri (site: taramasi)
            $"site:ticaretsicil.gov.tr {q}",
            $"site:ekap.kamuihale.gov.tr {q}",
        };

        return queries.Take(Math.Max(1, options.MaxNewsQueries)).ToList();
    }
}
