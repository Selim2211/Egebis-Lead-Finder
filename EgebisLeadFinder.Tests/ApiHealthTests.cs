using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class ApiHealthTests
{
    [Fact]
    public void Ok_responses_are_not_errors()
    {
        Assert.Null(ApiHealthService.Classify("groq", 200, "{\"data\":[]}"));
        Assert.Null(ApiHealthService.Classify("apollo", 200, "{\"healthy\":true,\"is_logged_in\":true}"));
        Assert.Null(ApiHealthService.Classify("serper", 200, "{\"balance\":1200}"));
        Assert.Null(ApiHealthService.Classify("serper", 404, "Not found")); // uc nokta degisti: erisim sorunu degil
    }

    [Fact]
    public void Apollo_unknown_key_is_reported()
    {
        var e = ApiHealthService.Classify("apollo", 200, "{\"healthy\":true,\"is_logged_in\":false}");
        Assert.Contains("geçersiz", e);
    }

    [Fact]
    public void Serper_zero_balance_is_reported() =>
        Assert.Contains("Kredi bitti", ApiHealthService.Classify("serper", 200, "{\"balance\":0}"));

    [Theory]
    [InlineData(401, "geçersiz")]
    [InlineData(402, "Kredi bitti")]
    [InlineData(403, "Erişim reddedildi")]
    [InlineData(429, "sınır")]
    [InlineData(503, "hata veriyor")]
    public void Error_statuses_are_reported(int status, string expected) =>
        Assert.Contains(expected, ApiHealthService.Classify("apollo", status, "{\"error\":\"x\"}"));

    [Fact]
    public void Gemini_invalid_key_400_is_reported()
    {
        var e = ApiHealthService.Classify("gemini", 400, "{\"error\":{\"message\":\"API key not valid. Please pass a valid API key.\"}}");
        Assert.Contains("geçersiz", e);
        Assert.Contains("HTTP 400", e);
    }
}
