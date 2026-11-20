using System.ComponentModel.DataAnnotations;

namespace EgebisLeadFinder.Models;

/// <summary>Haber akışındaki tek haber (borsa tarzı akışın bir satırı).</summary>
public class NewsFeedItem
{
    public long Id { get; set; }

    /// <summary>Takip haberlerinde ilgili firma; radar haberlerinde boş (firma henüz tanınmıyor).</summary>
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    public int? RadarTopicId { get; set; }
    public RadarTopic? RadarTopic { get; set; }

    [Required, MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Summary { get; set; }

    [Required, MaxLength(2000)]
    public string Url { get; set; } = string.Empty;

    /// <summary>Adresin normalleştirilmiş SHA-256 özeti: aynı haber iki kez yazılmaz.</summary>
    [Required, MaxLength(64)]
    public string UrlHash { get; set; } = string.Empty;

    /// <summary>Normalleştirilmiş başlık: farklı sitelerdeki aynı haberi eler.</summary>
    [MaxLength(200)]
    public string TitleKey { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? SourceName { get; set; }

    /// <summary>Haberin yayın zamanı (UTC); bilinmiyorsa toplanma zamanı.</summary>
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(20)]
    public string Category { get; set; } = NewsCategories.General;

    /// <summary>1 (düşük) - 5 (acil fırsat/risk).</summary>
    public int Importance { get; set; } = 1;

    [MaxLength(100)]
    public string? City { get; set; }

    /// <summary>Radar haberinde başlıkta geçen firma adı (yapay zekâ doldurur).</summary>
    [MaxLength(200)]
    public string? MentionedCompany { get; set; }

    [MaxLength(10)]
    public string Origin { get; set; } = NewsOrigins.Watch;

    /// <summary>"tr" = Türkiye haberi, "global" = yurt dışı (Haber Akışı'ndaki TR / Global seçimi).</summary>
    [MaxLength(10)]
    public string Region { get; set; } = NewsRegions.Tr;
}

public static class NewsRegions
{
    public const string Tr = "tr";
    public const string Global = "global";

    /// <summary>Henüz karar verilmedi (yapay zekâ sınıflayacak); TR ve Global seçiminde görünmez, Tümü'nde görünür.</summary>
    public const string Pending = "";

    /// <summary>Kaynak bölge bildirmediyse: .tr alan adı ya da Türkçe harf içeren başlık TR, diğerleri Global.</summary>
    public static string Classify(string? url, string? title)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.ToLowerInvariant() : string.Empty;
        if (host.EndsWith(".tr", StringComparison.Ordinal)) return Tr;
        return (title ?? string.Empty).IndexOfAny("çğıöşüÇĞİÖŞÜ".ToCharArray()) >= 0 ? Tr : Global;
    }

    /// <summary>Çerez/sorgu değeri: "tr", "global" ya da null (tümü).</summary>
    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Tr => Tr,
        Global => Global,
        _ => null
    };
}

public static class NewsOrigins
{
    public const string Watch = "watch";
    public const string Radar = "radar";
}

public static class NewsCategories
{
    public const string Investment = "yatirim";
    public const string Risk = "risk";
    public const string Finance = "finans";
    public const string Management = "yonetim";
    public const string Technology = "teknoloji";
    public const string General = "genel";

    public static readonly string[] All = { Investment, Risk, Finance, Management, Technology, General };

    public static string Label(string? category) => category switch
    {
        Investment => "Yatırım",
        Risk => "Risk",
        Finance => "Finans",
        Management => "Yönetim",
        Technology => "Teknoloji",
        _ => "Genel"
    };

    public const string Positive = "pozitif";
    public const string Negative = "negatif";
    public const string Neutral = "notr";

    /// <summary>Duygu etiketi kategoriden türetilir: yatırım olumlu, risk olumsuz, kalanı nötr.</summary>
    public static string Sentiment(string? category) => category switch
    {
        Investment => Positive,
        Risk => Negative,
        _ => Neutral
    };

    public static string SentimentLabel(string? category) => Sentiment(category) switch
    {
        Positive => "Pozitif",
        Negative => "Negatif",
        _ => "Nötr"
    };
}

/// <summary>Kullanıcının firma kartından "Takip et" dediği firma. Favoriler de takip sayılır.</summary>
public class CompanyWatch
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Radar konusu: "yeni fabrika yatırımı Manisa" gibi, firma bilmeden fırsat haberi arayan sorgu.</summary>
public class RadarTopic
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Query { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? City { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Dönüşümlü tarama sırası: her firma / radar konusu için en son ne zaman tarandığı. Kaydı olmayan "hiç taranmamış"tır
/// ve sırada öne geçer (yeni "Takip et" ve yeni radar konusu hemen taranır).
/// </summary>
public class NewsScanCursor
{
    public int Id { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(10)]
    public string Kind { get; set; } = NewsCursorKinds.Company;

    public int RefId { get; set; }
    public DateTime LastScannedAt { get; set; } = DateTime.UtcNow;
}

public static class NewsCursorKinds
{
    public const string Company = "company";
    public const string Topic = "topic";
}

/// <summary>Bir haber taramasının özeti: ne zaman, kaç haber, kaç kredi.</summary>
public class NewsScanRun
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public int Queries { get; set; }
    public int Found { get; set; }
    public int NewCount { get; set; }
    public int SerperCredits { get; set; }

    /// <summary>Bu taramada yapılan yapay zekâ çağrısı (günlük tavan için sayılır).</summary>
    public int AiCalls { get; set; }

    [MaxLength(500)]
    public string? Error { get; set; }
}
