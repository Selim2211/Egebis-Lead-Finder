using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

/// <summary>Sitedeki NACE islemlerinin 6 haneli kodla calismasi.</summary>
public class Nace6Tests
{
    [Fact]
    public void An_official_six_digit_code_is_kept_as_is() =>
        Assert.Equal("27.51.08", NaceCatalog.ResolveCode("275108"));

    [Fact]
    public void An_invented_six_digit_code_falls_back_to_its_four_digit_class() =>
        Assert.Equal("27.51", NaceCatalog.ResolveCode("27.51.99"));

    [Theory]
    [InlineData("27")]
    [InlineData("27.51")]
    public void Shorter_valid_codes_are_still_accepted(string code) =>
        Assert.Equal(code, NaceCatalog.ResolveCode(code));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("99.99.99")]
    public void Garbage_codes_are_rejected(string? code) =>
        Assert.Null(NaceCatalog.ResolveCode(code));

    [Fact]
    public void Name_of_a_six_digit_code_is_the_official_activity_name() =>
        Assert.False(string.IsNullOrWhiteSpace(NaceCatalog.NameOf("27.51.08")));

    [Fact]
    public void Activity_search_finds_by_code_prefix_and_by_word()
    {
        Assert.All(NaceCatalog.SearchActivities("27.51"), a => Assert.StartsWith("2751", a.Key));
        Assert.NotEmpty(NaceCatalog.SearchActivities("buzdolabı"));
    }

    [Theory]
    [InlineData("27", "27.51.08", true)]          // bolum -> 6 haneli firma
    [InlineData("27.51.08", "27.51.08", true)]    // ayni kod
    [InlineData("27.51.08", "27.51", true)]       // daha genel firma kodu elenmez
    [InlineData("27.51.08", "27.52.01", false)]   // baska faaliyet
    [InlineData("27.51.08", "10.11.01", false)]
    public void Icp_matches_in_both_directions(string icpCode, string companyCode, bool expected) =>
        Assert.Equal(expected, new IcpProfile { NaceCodes = { icpCode } }.MatchesNace(companyCode));
}
