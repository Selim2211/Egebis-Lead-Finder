using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class NaceSearchTests
{
    [Theory]
    [InlineData("27", "27")]
    [InlineData("275", "27.5")]
    [InlineData("2751", "27.51")]
    [InlineData("27.51", "27.51")]
    [InlineData("27.51.08", "27.51.08")]
    [InlineData("275108", "27.51.08")]
    [InlineData(" 27 51 08 ", "27.51.08")]
    public void NormalizeFull_accepts_2_3_4_and_6_digits(string input, string expected) =>
        Assert.Equal(expected, NaceCatalog.NormalizeFull(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("27511")]
    [InlineData("27.51.0")]
    [InlineData("abc")]
    public void NormalizeFull_rejects_other_lengths(string? input) =>
        Assert.Null(NaceCatalog.NormalizeFull(input));

    [Theory]
    [InlineData("27.51", "27.51.08", true)]   // firma sinifi aranan 6 haneli kodun sinifiyla ayni
    [InlineData("27.51", "27.51", true)]
    [InlineData("27.52", "27.51.08", false)]  // ayni bolum, baska sinif -> elenir
    [InlineData("22.19", "27.51.08", false)]
    [InlineData("27", "27.51.08", true)]      // firma kodu daha genel: elenmez
    [InlineData(null, "27.51.08", true)]      // firma kodu bilinmiyor: elenmez
    [InlineData("27.51", "27", true)]         // aranan genis (bolum): sinif eslesir
    [InlineData("28.11", "27", false)]
    public void MatchesTarget_drops_only_clearly_different_codes(string? company, string target, bool expected) =>
        Assert.Equal(expected, NaceCatalog.MatchesTarget(company, target));

    [Fact]
    public void Official_list_knows_the_example_code_and_rejects_made_up_ones()
    {
        // Yapay zeka bu kodu "aydinlatma" diye uydurmustu; resmi liste beyaz esyadir.
        var name = NaceCatalog.Activity("27.51.08");
        Assert.NotNull(name);
        Assert.Contains("buzdolabı", name);
        Assert.Equal(name, NaceCatalog.Activity("275108"));
        Assert.Null(NaceCatalog.Activity("27.51.99"));
        Assert.True(NaceCatalog.Activities.Count > 2000);
    }

    [Fact]
    public void ActivitiesUnder_lists_the_official_children_of_a_class()
    {
        var subs = NaceCatalog.ActivitiesUnder("27.51");
        Assert.NotEmpty(subs);
        Assert.All(subs, s => Assert.StartsWith("2751", s.Key));
        Assert.Contains(subs, s => s.Key == "275108");
        Assert.Empty(NaceCatalog.ActivitiesUnder("27.99"));
    }

    [Fact]
    public void ParseNaceDescribe_reads_fields_and_cleans_terms()
    {
        var r = GeminiAiService.ParseNaceDescribe("""
            {"name":"Beyaz eşya imalatı","shortName":"Beyaz eşya","description":"Ev tipi buzdolabı ve çamaşır makinesi imalatı.",
             "searchTerms":["buzdolabı üreticisi","\"çamaşır makinesi fabrikası\"","a","buzdolabı üreticisi"]}
            """);
        Assert.Null(r.Error);
        Assert.Equal("Beyaz eşya imalatı", r.Name);
        Assert.Equal("Beyaz eşya", r.ShortName);
        Assert.Equal(2, r.SearchTerms.Count); // tekrar ve cok kisa terim atilir, tirnaklar temizlenir
        Assert.Contains("çamaşır makinesi fabrikası", r.SearchTerms);
    }

    [Fact]
    public void ParseNaceDescribe_splits_terms_the_model_glued_together()
    {
        // Gercek hata: model iki terimi tek dizgede birlestirmisti: "LED lamba fabrikası","aydınlatma ekipmanı imalatı"
        var r = GeminiAiService.ParseNaceDescribe("""
            {"name":"X","shortName":"Y","description":"d","searchTerms":["elektrikli aydınlatma üreticisi","LED lamba fabrikası\",\"aydınlatma ekipmanı imalatı"]}
            """);
        Assert.Equal(new[] { "elektrikli aydınlatma üreticisi", "LED lamba fabrikası", "aydınlatma ekipmanı imalatı" }, r.SearchTerms);
    }

    [Fact]
    public void ParseNaceDescribe_reports_missing_name_and_bad_json()
    {
        Assert.NotNull(GeminiAiService.ParseNaceDescribe("""{"name":"","shortName":"","description":"d","searchTerms":[]}""").Error);
        Assert.NotNull(GeminiAiService.ParseNaceDescribe("not json").Error);
    }

    [Fact]
    public void AiIndustry_adds_nace_context_only_when_present()
    {
        var c = new SearchCriteria { Industry = "Beyaz eşya" };
        Assert.Equal("Beyaz eşya", c.AiIndustry);
        c.NaceContext = "NACE 27.51: Elektrikli ev aletleri imalatı";
        Assert.Equal("Beyaz eşya (NACE 27.51: Elektrikli ev aletleri imalatı)", c.AiIndustry);
    }

    private sealed class FakeAi : IInsightAi
    {
        public string? LastContext;
        public bool Fail;
        public Task<NaceDescribeResult> DescribeNaceAsync(string code, string? officialContext, CancellationToken ct = default)
        {
            LastContext = officialContext;
            return Task.FromResult(Fail
                ? new NaceDescribeResult { Error = "AI yok" }
                : new NaceDescribeResult { Name = "Ad", ShortName = "Beyaz eşya", Description = "Açıklama.", SearchTerms = { "buzdolabı üreticisi" } });
        }
        public Task<CompareAiResult> CompareCompaniesAsync(BusinessProfile? p, string a, string b, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NaceMatchAiResult> MatchNaceAsync(string k, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SectorAiResult> AnalyzeSectorsAsync(BusinessProfile? p, string k, IReadOnlyList<SectorInput> s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IcpSuggestion> SuggestIcpAsync(string u, string t, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static NaceLookupService Lookup(FakeAi ai) => new(ai, new MemoryCache(new MemoryCacheOptions()), NullLogger<NaceLookupService>.Instance);

    [Fact]
    public async Task Lookup_six_digit_uses_the_official_name_and_gives_the_ai_only_that()
    {
        var ai = new FakeAi();
        var info = await Lookup(ai).LookupAsync("27.51.08");
        Assert.True(info.Ok);
        Assert.True(info.Official);
        Assert.Contains("buzdolabı", info.Name);           // ad AI'dan degil resmi listeden
        Assert.Equal("Beyaz eşya", info.ShortName);
        Assert.Contains("Resmî faaliyet adı", ai.LastContext);
        Assert.Contains("buzdolabı", ai.LastContext);
    }

    [Fact]
    public async Task Lookup_rejects_codes_that_are_not_in_the_official_list()
    {
        var info = await Lookup(new FakeAi()).LookupAsync("27.51.99");
        Assert.False(info.Ok);
        Assert.Contains("resmî NACE", info.Error);
        Assert.False((await Lookup(new FakeAi()).LookupAsync("12345")).Ok);
        Assert.False((await Lookup(new FakeAi()).LookupAsync("27.99")).Ok);   // 27.99 diye sinif yok
    }

    [Fact]
    public async Task Lookup_class_code_passes_official_children_to_the_ai()
    {
        var ai = new FakeAi();
        var info = await Lookup(ai).LookupAsync("2751");
        Assert.True(info.Ok);
        Assert.False(info.Official);
        Assert.Contains("27.51.08", ai.LastContext);
        Assert.NotNull(info.Basis);
    }

    [Fact]
    public async Task Lookup_still_resolves_official_codes_when_the_ai_is_down()
    {
        var info = await Lookup(new FakeAi { Fail = true }).LookupAsync("27.51.08");
        Assert.True(info.Ok);
        Assert.Contains("buzdolabı", info.Name);
        Assert.Null(info.Description);
        Assert.Single(info.SearchTerms);                  // resmi adin ilk kismindan terim
        Assert.False(string.IsNullOrWhiteSpace(info.ShortName));
    }
}
