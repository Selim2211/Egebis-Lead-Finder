using EgebisLeadFinder.Services.Quality;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class ProducerEvalTests
{
    private static GoldItem Gold() => new()
    {
        Producers = new() { "arcelik.com.tr", "beko.com.tr", "vestel.com.tr" },
        NotProducers = new() { "trendyol.com" }
    };

    [Theory]
    [InlineData("https://www.Arcelik.com.tr/urunler?x=1", "arcelik.com.tr")]
    [InlineData("WWW.BEKO.COM.TR", "beko.com.tr")]
    [InlineData(" vestel.com.tr ", "vestel.com.tr")]
    [InlineData("http://shop.beko.com.tr/#top", "shop.beko.com.tr")]
    [InlineData(null, "")]
    public void NormalizeDomain_strips_scheme_www_path(string? input, string expected) =>
        Assert.Equal(expected, ProducerEval.NormalizeDomain(input));

    [Fact]
    public void Evaluate_counts_found_wrong_missed_and_unlabeled()
    {
        var r = ProducerEval.Evaluate(Gold(), new[] { "www.arcelik.com.tr", "shop.beko.com.tr", "trendyol.com", "bilinmeyen.com.tr", "arcelik.com.tr" });

        Assert.Equal(4, r.Returned);                                   // tekrar eden alan adi bir kez
        Assert.Equal(new[] { "arcelik.com.tr", "shop.beko.com.tr" }, r.Found);   // alt alan adi da sayilir
        Assert.Equal(new[] { "trendyol.com" }, r.Wrong);
        Assert.Equal(new[] { "bilinmeyen.com.tr" }, r.Unlabeled);
        Assert.Equal(new[] { "vestel.com.tr" }, r.Missed);
        Assert.Equal(2 / 3.0, r.Precision!.Value, 3);                  // 2 dogru / (2 dogru + 1 yanlis)
        Assert.Equal(2 / 3.0, r.Recall!.Value, 3);                     // 3 bilinen ureticiden 2'si
    }

    [Fact]
    public void Precision_and_recall_are_null_when_there_is_nothing_to_measure()
    {
        var onlyUnlabeled = ProducerEval.Evaluate(new GoldItem(), new[] { "a.com" });
        Assert.Null(onlyUnlabeled.Precision);   // etiketli firma yok
        Assert.Null(onlyUnlabeled.Recall);      // bilinen uretici yok

        var none = ProducerEval.Evaluate(Gold(), Array.Empty<string>());
        Assert.Equal(0.0, none.Recall);
        Assert.Null(none.Precision);
        Assert.Equal(3, none.Missed.Count);
    }

    [Fact]
    public void Perfect_result_is_one_hundred_percent()
    {
        var r = ProducerEval.Evaluate(Gold(), new[] { "arcelik.com.tr", "beko.com.tr", "vestel.com.tr" });
        Assert.Equal(1.0, r.Precision);
        Assert.Equal(1.0, r.Recall);
    }

    [Fact]
    public void Different_sites_with_similar_names_are_not_confused()
    {
        // "notbeko.com.tr", "beko.com.tr"nin alt alan adi degildir.
        Assert.False(ProducerEval.SameSite("notbeko.com.tr", "beko.com.tr"));
        Assert.True(ProducerEval.SameSite("tr.shop.beko.com.tr", "beko.com.tr"));
    }

    [Fact]
    public void ParseDomains_cleans_deduplicates_and_drops_non_domains()
    {
        var d = ProducerEval.ParseDomains("https://www.arcelik.com.tr/hakkimizda\nbeko.com.tr, BEKO.com.tr; kelime\n\n vestel.com.tr");
        Assert.Equal(new[] { "arcelik.com.tr", "beko.com.tr", "vestel.com.tr" }, d);
    }

    [Fact]
    public void Gold_set_roundtrips_through_json_and_bad_json_gives_an_empty_list()
    {
        var json = ProducerEval.Serialize(ProducerEval.Starter());
        var back = ProducerEval.Parse(json);
        Assert.Equal("27.51.08", back.Single().NaceCode);
        Assert.Contains("arcelik.com.tr", back.Single().Producers);

        Assert.Empty(ProducerEval.Parse("{bozuk"));
        Assert.Empty(ProducerEval.Parse(null));
    }

    [Fact]
    public void Starter_example_uses_a_valid_official_nace_code()
    {
        Assert.True(EgebisLeadFinder.Services.NaceCatalog.Exists(ProducerEval.Starter().Single().NaceCode));
    }
}
