using EgebisLeadFinder.Services.News;

namespace EgebisLeadFinder.Models;

/// <summary>Akıştaki bir satır: haber + (takip haberiyse) firma adı.</summary>
public record NewsRow(NewsFeedItem Item, string? CompanyName, int CompanyBoost = 0);

/// <summary>Ajan panelindeki sayaçlar (Türkiye günü).</summary>
public record NewsCounters(int Queries, int NewToday, int HotToday, int Watched);

/// <summary>Bir firmanın haber nabzı: puan, haber etkisi, mini grafik, olumlu/olumsuz dengesi.</summary>
public record CompanyPulse(int CompanyId, string Name, string? Industry, int Score, int Boost, int[] Spark,
    int Count7, int Count30, int Positive, int Negative, List<NewsTop> Top, DateTime? LastAt, bool IsMine);

public record NewsTop(string Title, string Url, int Importance);

/// <summary>Hareket hesabı için tek haber (takip firması ya da radarın adını geçirdiği firma).</summary>
public record MoverNews(int? CompanyId, string Name, string? Industry, int? Score, int Boost, string? City,
    DateTime PublishedAt, string Category, int Importance, string Title, string Url, string? Summary);

/// <summary>Terminaldeki firma hareketi: günlük/haftalık itibar etkisi, gün gün etki, yapay zeka özetleri.</summary>
public record CompanyMover(int? CompanyId, string Name, string? Industry, int? Score, int Boost,
    int DayImpact, int WeekImpact, int[] Daily, int CountToday, int Count7,
    string? Summary, NewsTop? Positive, NewsTop? Risk, NewsTop? Latest);

public record CategoryStat(string Category, int Count);

public record TopicStat(RadarTopic Topic, int Count7, DateTime? LastScannedAt, int[] Spark);

public record SectorStat(string Name, int Companies, int Count, int Positive, int Negative, List<NewsTop> Top, bool IsRadar);

/// <summary>Her modülün üstündeki ortak kabuğun verisi: ajan durumu, sayaçlar, şerit, aktif sekme.</summary>
public class NewsShellModel
{
    public string Active { get; init; } = "overview";
    public string? Q { get; init; }
    public bool IsAdmin { get; init; }
    public DateTime? SeenAt { get; init; }
    public long MaxId { get; init; }
    public AgentSnapshot Agent { get; init; } = new("idle", null, 0, 0, null, null, null);
    public NewsCounters Counters { get; init; } = new(0, 0, 0, 0);
    public int TickMinutes { get; init; } = 10;
    public List<CompanyMover> Strip { get; init; } = new();
    public List<NewsRow> Headlines { get; init; } = new();
    public NewsScanRun? LastRun { get; init; }

    /// <summary>Canlı kartların ekleneceği kapsayıcı kimliği; boşsa yalnızca durum/sayaç güncellenir.</summary>
    public string? InjectTarget { get; set; }
    public bool Compact { get; set; }
    public bool CanInject { get; set; }
}

public class NewsOverviewViewModel
{
    public required NewsShellModel Shell { get; init; }
    public List<NewsRow> Live { get; init; } = new();
    /// <summary>Manşet alanında sırayla dönen önemli haberler.</summary>
    public List<NewsRow> Hero { get; init; } = new();
    /// <summary>İtibar liderlik tablosu (haftalık ve günlük sıralama).</summary>
    public List<CompanyMover> Weekly { get; init; } = new();
    public List<CompanyMover> Daily { get; init; } = new();
}

public class NewsFeedViewModel
{
    public required NewsShellModel Shell { get; init; }
    public List<NewsRow> Rows { get; init; } = new();
    public string? Q { get; init; }
    public string Scope { get; init; } = "all";
    public string? Category { get; init; }
    public int Importance { get; init; }
    public int Days { get; init; } = 7;
    public int? CompanyId { get; init; }
    public string? CompanyName { get; init; }
    public int? TopicId { get; init; }
    public required PagerModel Pager { get; init; }
}

public class NewsCompaniesViewModel
{
    public required NewsShellModel Shell { get; init; }
    public List<CompanyPulse> Rows { get; init; } = new();
}

public class NewsSectorsViewModel
{
    public required NewsShellModel Shell { get; init; }
    public List<SectorStat> Sectors { get; init; } = new();
}

public class NewsRadarViewModel
{
    public required NewsShellModel Shell { get; init; }
    public List<TopicStat> Topics { get; init; } = new();
    public int? SelectedTopicId { get; init; }
    public List<NewsRow> Rows { get; init; } = new();
}
