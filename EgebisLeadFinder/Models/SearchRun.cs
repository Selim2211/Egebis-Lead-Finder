using System.ComponentModel.DataAnnotations;
using EgebisLeadFinder.Data;

namespace EgebisLeadFinder.Models;

public enum SearchRunKind
{
    /// <summary>Sektor + ulke/sehir ile serbest arama.</summary>
    Sector = 0,

    /// <summary>Belirli bir firmayi adiyla arama.</summary>
    Name = 1,

    /// <summary>Kayitli arama profiliyle yapilan arama.</summary>
    Profile = 2
}

public enum SearchRunStatus
{
    Running = 0,
    Done = 1,
    Cancelled = 2,

    /// <summary>Kota/servis hatasi nedeniyle yarida kaldi (bulunanlar kaydedildi).</summary>
    Aborted = 3,

    Failed = 4
}

/// <summary>
/// Bir firma aramasinin kaydi: kim, ne zaman, hangi kriterle aradi ve hangi firmalar geldi.
/// Firmalar ekrani varsayilan olarak son aramanin sonuclarini gosterir ("havuz" yerine).
/// </summary>
public class SearchRun
{
    public int Id { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    public SearchRunKind Kind { get; set; }

    [MaxLength(150)]
    public string? Industry { get; set; }

    [MaxLength(150)]
    public string? CompanyName { get; set; }

    [MaxLength(8)]
    public string? RegionKey { get; set; }

    public string? City { get; set; }

    public int? SearchProfileId { get; set; }
    public SearchProfile? SearchProfile { get; set; }

    /// <summary>Profil sonradan silinse de listede adi gorunsun.</summary>
    [MaxLength(150)]
    public string? ProfileName { get; set; }

    public SearchRunStatus Status { get; set; } = SearchRunStatus.Running;

    public int Found { get; set; }
    public int NewCount { get; set; }
    public int KnownCount { get; set; }
    public int FailedCount { get; set; }

    [MaxLength(500)]
    public string? AbortReason { get; set; }

    public List<SearchRunCompany> Companies { get; set; } = new();

    /// <summary>Uygulama yeniden baslayinca \"calisiyor\" kalan arama yarida kalmis sayilir.</summary>
    public bool IsStale(DateTime nowUtc) => Status == SearchRunStatus.Running && nowUtc - StartedAt > TimeSpan.FromHours(2);

    /// <summary>Kisa tanim: "Otomotiv · Almanya · Bavyera (profil: Ege Otomotiv)".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(CompanyName)) parts.Add($"“{CompanyName}”");
        if (!string.IsNullOrWhiteSpace(Industry)) parts.Add(Industry);
        parts.Add(SearchRegions.Get(RegionKey).Name);
        if (!string.IsNullOrWhiteSpace(City))
        {
            var cities = City.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            parts.Add(cities.Length <= 3 ? string.Join(", ", cities) : $"{string.Join(", ", cities.Take(3))} +{cities.Length - 3}");
        }
        var text = string.Join(" · ", parts);
        return ProfileName is null ? text : $"{text} (profil: {ProfileName})";
    }

    public string StatusLabel(DateTime nowUtc) => IsStale(nowUtc) ? "yarıda kaldı" : Status switch
    {
        SearchRunStatus.Running => "sürüyor",
        SearchRunStatus.Cancelled => "iptal edildi",
        SearchRunStatus.Aborted => "yarıda kaldı",
        SearchRunStatus.Failed => "hata",
        _ => "tamamlandı"
    };
}

/// <summary>Aramada bulunan firma: yeni mi eklendi, yoksa daha once kayitli miydi?</summary>
public class SearchRunCompany
{
    public int Id { get; set; }

    public int SearchRunId { get; set; }
    public SearchRun SearchRun { get; set; } = null!;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public bool IsNew { get; set; }
}
