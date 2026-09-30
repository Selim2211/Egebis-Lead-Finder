using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

/// <summary>Faz-II madde 5: sirket sitesinden ICP onerisi.</summary>
public class IcpSuggestionTests
{
    [Fact]
    public void Oneri_cozulur_gecersiz_kod_ve_il_ayiklanir()
    {
        var json = """
            {"companySummary":"Üretim takip yazılımı","idealCustomer":"Orta ölçekli üreticiler",
             "nace":[{"code":"22.22","name":"Plastik ambalaj","reason":"seri üretim"},
                     {"code":"25","name":"Metal ürünler","reason":"atölye"},
                     {"code":"00.1","name":"uydurma","reason":"-"},
                     {"code":"2222","name":"tekrar","reason":"-"}],
             "industryKeywords":["plastik"," metal ","plastik"],
             "countries":["Türkiye"],"cities":["izmir","Bursa","Atlantis"],
             "minEmployees":-5,"minEmployeesReason":"-","requireManufacturer":true,"manufacturerReason":"üretim",
             "excludeKeywords":["bayi"],"targetTitles":["Fabrika Müdürü"]}
            """;
        var s = GeminiAiService.ParseIcpSuggestion(json);

        Assert.True(s.Success);
        Assert.Equal(new[] { "22.22", "25" }, s.Nace.Select(n => n.Code));
        Assert.Equal(new[] { "22", "25" }, s.DivisionCodes);
        Assert.Equal(new[] { "plastik", "metal" }, s.IndustryKeywords);
        Assert.Equal(new[] { "İzmir", "Bursa" }, s.Cities);
        Assert.Equal(0, s.MinEmployees);
        Assert.True(s.RequireManufacturer);
    }

    [Fact]
    public void Bozuk_yanit_hata_doner()
    {
        Assert.False(GeminiAiService.ParseIcpSuggestion("bozuk").Success);
    }
}
