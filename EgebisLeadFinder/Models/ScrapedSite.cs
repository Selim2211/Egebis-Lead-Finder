namespace EgebisLeadFinder.Models;

/// <summary>WebScraperService ciktisi: bir firma sitesinden toplanan ham veri.</summary>
public class ScrapedSite
{
    public string Url { get; set; } = string.Empty;

    /// <summary>Temizlenmis, AI'a gonderilmeye hazir duz metin.</summary>
    public string Text { get; set; } = string.Empty;

    public List<string> Emails { get; set; } = new();
    public List<string> Phones { get; set; } = new();

    /// <summary>Sitede bulunan aday kisi/unvan ciftleri.</summary>
    public List<ScrapedPerson> People { get; set; } = new();

    public List<string> VisitedUrls { get; set; } = new();
    public string? Error { get; set; }
    public bool Success => Error is null && Text.Length > 0;
}

public class ScrapedPerson
{
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? SourceUrl { get; set; }
}
