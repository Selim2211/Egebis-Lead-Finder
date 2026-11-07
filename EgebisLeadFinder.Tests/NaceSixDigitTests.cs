using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class NaceSixDigitTests
{
    [Theory]
    [InlineData("27.51.08", "27.51.08", NaceFit.Yes)]    // ayni 6 haneli kod
    [InlineData("27.51.02", "27.51.08", NaceFit.No)]     // ayni sinif, baska alt faaliyet -> eslesmez
    [InlineData("27.51", "27.51.08", NaceFit.Partial)]   // firma kodu daha genel: alt faaliyet belirlenmeli
    [InlineData("27", "27.51.08", NaceFit.Partial)]
    [InlineData(null, "27.51.08", NaceFit.Partial)]      // bilinmiyor
    [InlineData("22.19", "27.51.08", NaceFit.No)]
    [InlineData("27.51.08", "27.51", NaceFit.Yes)]       // firma kodu daha ayrintili: aranan sinifa dahil
    [InlineData("27.51.02", "27.51", NaceFit.Yes)]
    [InlineData("27.52", "27.51", NaceFit.No)]
    [InlineData("27.51", "27", NaceFit.Yes)]
    public void Compare_is_prefix_based_up_to_six_digits(string? company, string target, NaceFit expected) =>
        Assert.Equal(expected, NaceCatalog.Compare(company, target));

    [Theory]
    [InlineData("27.51.08", "27.51.08")]
    [InlineData("275108", "27.51.08")]
    [InlineData("22.19", "22.19")]
    [InlineData("2219", "22.19")]
    [InlineData("22.19 Plastik ürünler", "22.19")]   // serbest metinden yalniz ilk 4 hane
    [InlineData("22", "22")]
    public void Normalize_keeps_a_full_six_digit_code(string input, string expected) =>
        Assert.Equal(expected, NaceCatalog.Normalize(input));

    [Fact]
    public void Label_uses_the_official_name_for_six_digit_codes()
    {
        var label = NaceCatalog.Label("27.51.08");
        Assert.StartsWith("27.51.08 · ", label);
        Assert.Contains("buzdolabı", label);
    }

    [Theory]
    [InlineData("27.51", true)]
    [InlineData("27.51.08", true)]
    [InlineData("27.5", true)]
    [InlineData("27", true)]
    [InlineData("20.22", false)]    // gercek hata: kozmetik firmasina atanmisti, Rev.2'de yok
    [InlineData("28.32", false)]    // gercek hata: Arcelik Kurutucu/Elektrik Motorlari'na atanmisti
    [InlineData("81.20", false)]    // gercek hata: dernege atanmisti
    [InlineData("27.51.99", false)]
    [InlineData("99", true)]
    [InlineData("1", false)]
    public void Exists_rejects_made_up_classes(string code, bool expected) =>
        Assert.Equal(expected, NaceCatalog.Exists(code));

    // ---------------- Urun listesi

    private static string Official2751_08 => NaceCatalog.Activity("27.51.08")!;

    [Fact]
    public void Split_turns_the_official_text_into_single_products()
    {
        var products = NaceProducts.Split(Official2751_08);
        Assert.Contains("buzdolabı", products);
        Assert.Contains("bulaşık makinesi", products);
        Assert.Contains("çamaşır makinesi", products);
        Assert.Contains("mikrodalga fırın", products);
        Assert.DoesNotContain(products, p => p.Contains("imalatı") || p.Contains("vb") || p.StartsWith("ev tipi", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("bulaşık makinesi", true)]
    [InlineData("Bulaşık Makineleri", true)]   // cogul / buyuk harf: kok ayni
    [InlineData("dondurucu", true)]
    [InlineData("derin dondurucu", false)]     // resmi metinde "derin" yok
    [InlineData("televizyon", false)]          // metinde yok: yapay zeka eklemis
    [InlineData("ev tipi", false)]             // yalniz dolgu kelime
    public void IsGrounded_accepts_only_products_written_in_the_official_text(string product, bool expected) =>
        Assert.Equal(expected, NaceProducts.IsGrounded(product, Official2751_08));

    [Fact]
    public void Resolve_keeps_only_allowed_products_and_falls_back_to_all()
    {
        var allowed = new[] { "buzdolabı", "bulaşık makinesi", "çamaşır makinesi" };
        Assert.Equal(new[] { "bulaşık makinesi" }, NaceProducts.Resolve("Bulaşık Makinesi, televizyon", allowed));
        Assert.Equal(allowed, NaceProducts.Resolve(null, allowed));
        Assert.Equal(allowed, NaceProducts.Resolve("televizyon", allowed));   // liste disi secim: tum liste
    }

    // ---------------- Urun eslestirme (firma bu urunlerden hangilerini uretiyor?)

    private sealed class FakeMatcher : INaceClassifierAi
    {
        public Dictionary<int, ProductVerdict> Answer = new();
        public bool Fail;
        public IReadOnlyList<string>? Products;
        public Task<Dictionary<int, string>> ClassifyNaceAsync(IReadOnlyList<(int Id, string Text)> c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Dictionary<int, ProductVerdict>> MatchProductsAsync(IReadOnlyList<(int Id, string Text)> c, IReadOnlyList<string> products, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("AI kesintisi");
            Products = products;
            return Task.FromResult(Answer);
        }
    }

    private static Company Firm(string name) => new() { Name = name, Industry = "Beyaz eşya" };
    private static readonly string[] WhiteGoods = { "buzdolabı", "bulaşık makinesi", "çamaşır makinesi" };

    [Fact]
    public async Task MatchProducts_keeps_makers_of_at_least_one_product_and_gives_reasons_for_the_rest()
    {
        var ai = new FakeMatcher
        {
            Answer =
            {
                [1] = new(new() { "bulaşık makinesi" }, null),
                [2] = new(new() { "Çamaşır Makinesi", "buzdolabı" }, null),
                [3] = new(new(), "bayi"),
                [4] = new(new() { "televizyon" }, null)          // liste disi urun: sayilmaz
            }
        };
        var icp = new IcpService(null!, null!, null!, ai);
        var a = Firm("Bulasik fabrikasi"); var b = Firm("Beyaz esya markasi"); var c = Firm("Bayi"); var d = Firm("TV");

        var r = await icp.MatchProductsAsync(new[] { a, b, c, d }, WhiteGoods);

        Assert.True(r.Ok);
        Assert.Equal(new[] { "bulaşık makinesi" }, r.Matches[a]);
        Assert.Equal(new[] { "çamaşır makinesi", "buzdolabı" }, r.Matches[b]);   // listedeki yazimla
        Assert.Equal("bayi", r.Reasons[c]);
        Assert.False(r.Matches.ContainsKey(d));
        Assert.True(r.Reasons.ContainsKey(d));
        Assert.Equal(WhiteGoods, ai.Products);
    }

    [Fact]
    public async Task MatchProducts_reports_failure_instead_of_dropping_everyone_when_the_ai_is_down()
    {
        var icp = new IcpService(null!, null!, null!, new FakeMatcher { Fail = true });
        var r = await icp.MatchProductsAsync(new[] { Firm("A") }, WhiteGoods);
        Assert.False(r.Ok);
        Assert.Empty(r.Reasons);
    }

    [Fact]
    public void ParseProductVerdicts_reads_products_and_reason()
    {
        var v = GeminiAiService.ParseProductVerdicts("""
            [{"id":1,"products":["bulaşık makinesi"," buzdolabı "]},{"id":2,"products":[],"reason":"yan sanayi (parça)"}]
            """);
        Assert.Equal(new[] { "bulaşık makinesi", "buzdolabı" }, v[1].Products);
        Assert.Empty(v[2].Products);
        Assert.Equal("yan sanayi (parça)", v[2].Reason);
    }

    [Fact]
    public void Screen_text_names_the_products_to_be_made()
    {
        var c = new SearchCriteria { Industry = "Beyaz eşya", ProductList = { "bulaşık makinesi", "buzdolabı" } };
        Assert.Contains("bulaşık makinesi, buzdolabı", c.ScreenIndustry);
        Assert.Equal("Otomotiv", new SearchCriteria { Industry = "Otomotiv" }.ScreenIndustry);
    }
}
