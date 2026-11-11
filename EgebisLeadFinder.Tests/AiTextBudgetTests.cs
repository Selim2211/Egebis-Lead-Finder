using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class AiTextBudgetTests
{
    private static string Marquee(int times) =>
        string.Join(" ✦ ", Enumerable.Repeat("Uğur Derin Dondurucular 7 Yıl Garantili !!!", times));

    [Fact]
    public void Repeated_marquee_text_collapses_to_one_copy()
    {
        var result = TextExtractor.CollapseRepeats(Marquee(24) + "\nÜrünler Kampanyalar");

        Assert.Equal(1, result.Split("7 Yıl Garantili").Length - 1);
        Assert.Contains("Ürünler Kampanyalar", result);
    }

    [Fact]
    public void Duplicate_lines_are_dropped_but_order_is_kept()
    {
        var result = TextExtractor.CollapseRepeats("Menü Ürünler\nHakkımızda\nMenü Ürünler\nİletişim");

        Assert.Equal(new[] { "Menü Ürünler", "Hakkımızda", "İletişim" }, result.Split('\n').Select(l => l.Trim()));
    }

    [Fact]
    public void A_long_homepage_does_not_push_the_about_page_out_of_the_ai_text()
    {
        // Ana sayfa vitrin/menu metniyle butun payi tek basina doldurur; eskiden "Hakkimizda" yapay zekaya hic ulasmiyordu.
        var home = string.Join("\n", Enumerable.Range(0, 2000).Select(i => $"Ürün {i} sepete ekle"));
        var about = "Uğur Soğutma A.Ş. Nazilli'deki üretim tesisinde derin dondurucu üretir.";

        var text = WebScraperService.BuildAiText(new[] { ("https://x.com.tr/", home), ("https://x.com.tr/hakkimizda", about) }, 8000);

        Assert.Contains("üretim tesisinde", text);
        Assert.True(text.Length <= 8000 + 400);
    }

    [Fact]
    public void Short_pages_leave_their_unused_share_to_longer_ones()
    {
        var pages = new[]
        {
            ("https://x.com/a", "kısa sayfa"),
            ("https://x.com/b", string.Join("\n", Enumerable.Range(0, 3000).Select(i => $"satır {i}"))),
        };

        var text = WebScraperService.BuildAiText(pages, 6000);

        Assert.Contains("kısa sayfa", text);
        Assert.True(text.Length > 5000);
    }

    [Fact]
    public void No_pages_gives_empty_text() =>
        Assert.Equal(string.Empty, WebScraperService.BuildAiText(Array.Empty<(string, string)>(), 8000));
}
