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

    private sealed class FakeChooser : INaceClassifierAi
    {
        public Dictionary<int, string> Answer = new();
        public bool Fail;
        public IReadOnlyList<KeyValuePair<string, string>>? Options;
        public Task<Dictionary<int, string>> ClassifyNaceAsync(IReadOnlyList<(int Id, string Text)> c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Dictionary<int, string>> ChooseNaceAsync(IReadOnlyList<(int Id, string Text)> c, IReadOnlyList<KeyValuePair<string, string>> options, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("AI kesintisi");
            Options = options;
            return Task.FromResult(Answer);
        }
    }

    private static Company Firm(string name, string? nace = "27.51") => new() { Name = name, NaceCode = nace, Industry = "Beyaz eşya" };

    [Fact]
    public async Task Refine_picks_from_the_official_class_list_and_writes_the_six_digit_code()
    {
        var ai = new FakeChooser { Answer = { [1] = "27.51.08", [2] = "27.51.02", [3] = "" } };
        var icp = new IcpService(null!, null!, null!, ai);
        var a = Firm("Beyaz esya fabrikasi"); var b = Firm("Su isitici fabrikasi"); var c = Firm("Bayi");

        var unresolved = await icp.RefineNaceAsync(new[] { a, b, c }, "27.51.08");

        Assert.Equal("27.51.08", a.NaceCode);
        Assert.Equal("27.51.02", b.NaceCode);               // baska alt faaliyet: yazilir, sonra aranan kodla eslesmez
        Assert.Equal(NaceFit.No, NaceCatalog.Compare(b.NaceCode, "27.51.08"));
        Assert.Equal(new[] { c }, unresolved);              // hicbiri uymadi
        Assert.All(ai.Options!, o => Assert.StartsWith("2751", o.Key));   // secenekler yalniz aranan sinifin resmi alt kodlari
    }

    [Fact]
    public async Task Refine_ignores_codes_outside_the_official_options()
    {
        var ai = new FakeChooser { Answer = { [1] = "27.51.99", [2] = "22.19.01" } };   // uydurma / baska sinif
        var icp = new IcpService(null!, null!, null!, ai);
        var a = Firm("A"); var b = Firm("B");

        var unresolved = await icp.RefineNaceAsync(new[] { a, b }, "27.51.08");

        Assert.Equal(2, unresolved.Count);
        Assert.Equal("27.51", a.NaceCode);                  // degismedi
    }

    [Fact]
    public async Task Refine_does_not_drop_anyone_when_the_ai_is_down()
    {
        var icp = new IcpService(null!, null!, null!, new FakeChooser { Fail = true });
        var a = Firm("A");
        Assert.Empty(await icp.RefineNaceAsync(new[] { a }, "27.51.08"));
        Assert.Equal("27.51", a.NaceCode);
    }

    [Fact]
    public async Task Refine_runs_for_class_and_six_digit_targets_but_not_for_a_whole_division()
    {
        var ai = new FakeChooser { Answer = { [1] = "27.51.08" } };
        var icp = new IcpService(null!, null!, null!, ai);

        var a = Firm("A", nace: "28.32");                   // eski yanlis/uydurma tahmin kod: hesaba katilmaz
        Assert.Empty(await icp.RefineNaceAsync(new[] { a }, "27.51"));
        Assert.Equal("27.51.08", a.NaceCode);               // 4 haneli aramada da resmi listeden secilir

        ai.Options = null;
        Assert.Empty(await icp.RefineNaceAsync(new[] { Firm("B") }, "27"));
        Assert.Null(ai.Options);                            // bolum (2 hane) icin yapay zeka cagrilmaz
    }

    [Theory]
    [InlineData("27.51", true)]
    [InlineData("27.51.08", true)]
    [InlineData("27.5", true)]
    [InlineData("27", true)]
    [InlineData("20.22", false)]    // gercek hata: kozmetik firmasina atanmisti, Rev.2'de yok
    [InlineData("28.32", false)]    // gercek hata: Arcelik Kurutucu/Elektrik Motorlari'na atanmisti
    [InlineData("81.20", false)]    // gercek hata: derneğe atanmisti
    [InlineData("27.51.99", false)]
    [InlineData("99", true)]
    [InlineData("1", false)]
    public void Exists_rejects_made_up_classes(string code, bool expected) =>
        Assert.Equal(expected, NaceCatalog.Exists(code));
}
