using System.Security.Cryptography;
using System.Text;

namespace EgebisLeadFinder.Services.Security;

/// <summary>
/// Veritabanindaki kritik alanlarin (kisi e-posta/telefonu, kullanici bilgileri, API anahtarlari)
/// sifrelenmesi: AES-256-GCM, her deger icin rastgele nonce. Sifreli deger "enc:v1:" on ekiyle
/// saklanir; on eki olmayan eski duz metin okunurken oldugu gibi doner (kademeli gecis).
///
/// Anahtar "Encryption:Key" (base64, 32 bayt) ayarindan veya anahtar dosyasindan gelir; dosya yoksa
/// ilk acilista olusturulur. ANAHTAR KAYBOLURSA SIFRELI VERI OKUNAMAZ: dosya yedeklenmelidir.
/// </summary>
public sealed class FieldEncryption
{
    public const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;
    private readonly byte[] _indexKey;

    /// <summary>
    /// Uygulama acilisinda ayarlanir. EF deger donusturuculeri modelle birlikte onbellege alindigi
    /// icin anahtara bu statik ornek uzerinden ulasir. Ayarlanmamissa (birim testleri) deger
    /// oldugu gibi saklanir.
    /// </summary>
    public static FieldEncryption? Current { get; set; }

    public FieldEncryption(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("Şifreleme anahtarı 32 bayt (256 bit) olmalı.", nameof(key));
        _key = key;
        // Arama indeksi icin ayri anahtar: sifreleme anahtari dogrudan HMAC'te kullanilmaz.
        _indexKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, info: Encoding.UTF8.GetBytes("egebis-blind-index-v1"));
    }

    /// <summary>Anahtar parmak izi (ilk 8 hane): log ve teshis icin, anahtarin kendisini acmaz.</summary>
    public string KeyId => Convert.ToHexString(SHA256.HashData(_indexKey))[..8].ToLowerInvariant();

    public static bool IsEncrypted(string? value) => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string? Encrypt(string? plain)
    {
        // Bos deger sifrelenmez: "bos mu?" sorgulari (Email == "") calismaya devam eder.
        if (string.IsNullOrEmpty(plain) || IsEncrypted(plain)) return plain;

        var data = Encoding.UTF8.GetBytes(plain);
        var buffer = new byte[NonceSize + TagSize + data.Length];
        var nonce = buffer.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, buffer.AsSpan(NonceSize + TagSize), buffer.AsSpan(NonceSize, TagSize));
        return Prefix + Convert.ToBase64String(buffer);
    }

    /// <summary>Sifreli degeri cozer; duz metni oldugu gibi dondurur. Anahtar yanlissa CryptographicException.</summary>
    public string? Decrypt(string? stored)
    {
        if (!IsEncrypted(stored)) return stored;

        var buffer = Convert.FromBase64String(stored![Prefix.Length..]);
        if (buffer.Length < NonceSize + TagSize) throw new CryptographicException("Şifreli değer bozuk.");

        var plain = new byte[buffer.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(buffer.AsSpan(0, NonceSize), buffer.AsSpan(NonceSize + TagSize), buffer.AsSpan(NonceSize, TagSize), plain);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>
    /// Sifreli alanda esitlik aramasi icin "kor indeks": normalize edilmis degerin HMAC'i.
    /// Ayni e-posta her zaman ayni indeksi verir; indeksten e-posta geri elde edilemez.
    /// </summary>
    public string? Index(string? value) => IndexWith(_indexKey, value);

    // ---------- statik kisayollar (EF donusturuculeri ve servisler) ----------

    public static string? Protect(string? plain) => Current is null ? plain : Current.Encrypt(plain);

    /// <summary>Okuma: anahtar yanlis/eksikse uygulama cokmez, alan "(şifre çözülemedi)" gorunur.</summary>
    public static string? Unprotect(string? stored)
    {
        if (!IsEncrypted(stored)) return stored;
        if (Current is null) return DecryptFailed;
        try
        {
            return Current.Decrypt(stored);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return DecryptFailed;
        }
    }

    public const string DecryptFailed = "(şifre çözülemedi)";

    /// <summary>Kor indeks; anahtar yoksa (birim testleri) anahtarsiz SHA-256.</summary>
    public static string? BlindIndex(string? value) =>
        Current is null ? IndexWith(null, value) : Current.Index(value);

    private static string? IndexWith(byte[]? key, string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized)) return null;
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = key is null ? SHA256.HashData(bytes) : HMACSHA256.HashData(key, bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Anahtari yukler: once "Encryption:Key" ayari, yoksa anahtar dosyasi; dosya da yoksa yeni
    /// anahtar uretip dosyaya yazar (created = true).
    /// </summary>
    public static (FieldEncryption Encryption, string Source, bool Created) Load(string? configuredKey, string keyFile)
    {
        if (!string.IsNullOrWhiteSpace(configuredKey))
            return (new FieldEncryption(Convert.FromBase64String(configuredKey.Trim())), "Encryption:Key ayarı", false);

        if (File.Exists(keyFile))
            return (new FieldEncryption(Convert.FromBase64String(File.ReadAllText(keyFile).Trim())), keyFile, false);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyFile))!);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(keyFile, Convert.ToBase64String(key));
        return (new FieldEncryption(key), keyFile, true);
    }
}
