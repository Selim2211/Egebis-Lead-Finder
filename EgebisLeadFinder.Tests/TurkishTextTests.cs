using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Tests;

public class TurkishTextTests
{
    [Theory]
    [InlineData("İSTANBUL", "istanbul")]
    [InlineData("Bilgi İşlem Müdürü", "bilgi islem muduru")]
    [InlineData("ÇAĞRI ÖZTÜRK", "cagri ozturk")]
    [InlineData("Iğdır", "igdir")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Turkce_harfleri_sadelestirir(string? input, string expected)
    {
        Assert.Equal(expected, TurkishText.Normalize(input));
    }

    [Fact]
    public void Normalizasyon_karakter_sayisini_degistirmez()
    {
        // CleanTitle normalize metindeki indeksi ham metinde kullaniyor;
        // uzunluk degisirse o mantik bozulur.
        const string input = "İŞÇİ ÖĞÜT ÇAĞ";
        Assert.Equal(input.Length, TurkishText.Normalize(input).Length);
    }

    [Fact]
    public void Buyuk_I_harfi_dogru_eslesir()
    {
        // ToLowerInvariant burada birlesik noktali "i" uretir ve eslesme kacar.
        Assert.True(TurkishText.ContainsNormalized("Bilgi İşlem Müdürü", "bilgi işlem müdürü"));
        Assert.True(TurkishText.ContainsNormalized("BİLGİ İŞLEM", "bilgi islem"));
    }

    [Fact]
    public void Alakasiz_metinde_eslesme_bulmaz()
    {
        Assert.False(TurkishText.ContainsNormalized("Satın Alma Uzmanı", "bilgi işlem"));
        Assert.False(TurkishText.ContainsNormalized("herhangi bir metin", ""));
    }
}
