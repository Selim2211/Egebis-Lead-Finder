using System.Security.Cryptography;
using EgebisLeadFinder.Services.Security;

namespace EgebisLeadFinder.Tests;

/// <summary>Faz-II madde 7: kritik alanlar sifreli saklanir.</summary>
public class FieldEncryptionTests
{
    private static FieldEncryption NewKey() => new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Sifreli_deger_cozulur_ve_her_seferinde_farkli_uretilir()
    {
        var enc = NewKey();
        var a = enc.Encrypt("ali.kaya@firma.com.tr");
        var b = enc.Encrypt("ali.kaya@firma.com.tr");

        Assert.StartsWith(FieldEncryption.Prefix, a);
        Assert.NotEqual(a, b);
        Assert.DoesNotContain("firma", a);
        Assert.Equal("ali.kaya@firma.com.tr", enc.Decrypt(a));
        Assert.Equal("Çağrı Öztürk", enc.Decrypt(enc.Encrypt("Çağrı Öztürk")));
    }

    [Fact]
    public void Bos_ve_eski_duz_metin_oldugu_gibi_kalir_iki_kez_sifrelenmez()
    {
        var enc = NewKey();
        Assert.Null(enc.Encrypt(null));
        Assert.Equal("", enc.Encrypt(""));
        Assert.Equal("eski@duz.com", enc.Decrypt("eski@duz.com"));

        var once = enc.Encrypt("x@y.com");
        Assert.Equal(once, enc.Encrypt(once));
    }

    [Fact]
    public void Baska_anahtar_veya_bozulmus_veri_cozulemez()
    {
        var enc = NewKey();
        var cipher = enc.Encrypt("gizli")!;

        Assert.ThrowsAny<CryptographicException>(() => NewKey().Decrypt(cipher));

        var bytes = Convert.FromBase64String(cipher[FieldEncryption.Prefix.Length..]);
        bytes[^1] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => enc.Decrypt(FieldEncryption.Prefix + Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Kor_indeks_buyuk_kucuk_harf_ve_bosluktan_bagimsiz_anahtara_bagli()
    {
        var enc = NewKey();
        Assert.Equal(enc.Index("Ali@Firma.com "), enc.Index("ali@firma.com"));
        Assert.NotEqual(enc.Index("ali@firma.com"), NewKey().Index("ali@firma.com"));
        Assert.Null(enc.Index("  "));
        Assert.Equal(64, enc.Index("a@b.c")!.Length);
    }

    [Fact]
    public void Anahtar_dosyasi_yoksa_uretilir_varsa_ayni_anahtar_okunur()
    {
        var dir = Path.Combine(Path.GetTempPath(), "egebis-enc-" + Guid.NewGuid().ToString("n"));
        var file = Path.Combine(dir, "field-encryption.key");
        try
        {
            var (first, _, created) = FieldEncryption.Load(null, file);
            Assert.True(created);
            var cipher = first.Encrypt("deneme");

            var (second, _, createdAgain) = FieldEncryption.Load(null, file);
            Assert.False(createdAgain);
            Assert.Equal("deneme", second.Decrypt(cipher));
            Assert.Equal(first.KeyId, second.KeyId);

            var fromConfig = FieldEncryption.Load(File.ReadAllText(file), Path.Combine(dir, "yok.key")).Encryption;
            Assert.Equal("deneme", fromConfig.Decrypt(cipher));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Kisa_anahtar_reddedilir()
    {
        Assert.Throws<ArgumentException>(() => new FieldEncryption(new byte[16]));
    }
}
