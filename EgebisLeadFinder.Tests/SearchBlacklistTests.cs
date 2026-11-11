using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class SearchBlacklistTests
{
    [Fact]
    public void Lines_are_split_into_domains_and_company_names()
    {
        var list = SearchBlacklist.Parse("https://www.ornek-firma.com/hakkimizda\nÖrnek Makina A.Ş.\r\nbaska.com.tr, Tolon");

        Assert.Equal(new[] { "ornek-firma.com", "baska.com.tr" }, list.Domains);
        Assert.Equal(2, list.Names.Count);
    }

    [Theory]
    [InlineData("ornek.com", true)]
    [InlineData("www.ornek.com", true)]
    [InlineData("shop.ornek.com", true)]
    [InlineData("notornek.com", false)]
    [InlineData("ornek.com.tr", false)]
    [InlineData(null, false)]
    public void Domain_match_covers_subdomains_only(string? domain, bool expected) =>
        Assert.Equal(expected, SearchBlacklist.Parse("ornek.com").MatchesDomain(domain));

    [Theory]
    [InlineData("TOLON MAKİNA SANAYİ", true)]
    [InlineData("tolon makina", true)]
    [InlineData("Tolonya Tekstil", false)]
    [InlineData("Başka Firma", false)]
    public void Name_match_ignores_case_and_turkish_letters_and_needs_whole_words(string text, bool expected) =>
        Assert.Equal(expected, SearchBlacklist.Parse("Tolon").MatchesName(text));

    [Fact]
    public void Company_name_with_several_words_never_blocks_by_one_word_or_domain()
    {
        var list = SearchBlacklist.Parse("Örnek Makina");

        Assert.Empty(list.Domains);
        Assert.False(list.Matches("makina.com", "Makina Sanayi"));
        Assert.True(list.Matches("abc.com", "ORNEK MAKINA Ltd"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n , ;")]
    [InlineData("x")]
    public void Empty_or_too_short_entries_block_nothing(string? text)
    {
        var list = SearchBlacklist.Parse(text);
        Assert.True(list.IsEmpty);
        Assert.False(list.Matches("ornek.com", "Herhangi Firma"));
    }
}
