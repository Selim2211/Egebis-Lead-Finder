namespace EgebisLeadFinder.Models;

/// <summary>Uygunluk puani (Company.FitScore) icin etiket ve renk sinifi.</summary>
public static class FitDisplay
{
    /// <summary>Bunun altinda Apollo e-posta acmadan once uyari gosterilir.</summary>
    public const int LowThreshold = 40;

    public static string Label(int score) => score switch
    {
        >= 80 => "Çok uygun",
        >= 60 => "Uygun",
        >= LowThreshold => "Kısmen uygun",
        >= 20 => "Zayıf uyum",
        _ => "Uygun değil"
    };

    public static string Css(int score) => score switch
    {
        >= 80 => "fit-high",
        >= 60 => "fit-good",
        >= LowThreshold => "fit-mid",
        _ => "fit-low"
    };
}
