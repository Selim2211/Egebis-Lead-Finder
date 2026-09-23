using System.Security.Cryptography;
using System.Text;
using EgebisLeadFinder.Services;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>
/// "Salesforce'a Bağlan" OAuth akışı (Authorization Code + PKCE). Kullanıcıyı
/// Salesforce'un kendi giriş/izin ekranına yönlendirir; onay sonrası Callback'e
/// dönen "code" access/refresh token ile değiştirilir (bkz. ISalesforceConnector).
/// Şifre bizim sistemimize hiç girilmez.
/// </summary>
public class SalesforceController : Controller
{
    private const string StateSessionKey = "SalesforceOAuthState";
    private const string VerifierSessionKey = "SalesforceOAuthVerifier";

    private readonly ISalesforceConnector _salesforce;
    private readonly ISettingsService _settings;

    public SalesforceController(ISalesforceConnector salesforce, ISettingsService settings)
    {
        _salesforce = salesforce;
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> Connect(CancellationToken ct)
    {
        // Dis adres tanimliysa ve istek baska bir adresten (ör. localhost) geldiyse once
        // oraya yonlendir: callback o adrese donecek ve state/verifier'i tutan oturum
        // cerezi ayni domain'de olmali, yoksa dogrulama basarisiz olur.
        var publicBase = await PublicBaseUrlAsync(ct);
        if (publicBase is not null && !string.Equals(CurrentBaseUrl(), publicBase, StringComparison.OrdinalIgnoreCase))
            return Redirect($"{publicBase}/Salesforce/Connect");

        var state = Guid.NewGuid().ToString("N");

        // PKCE: rastgele bir code_verifier uretilir, SHA-256/base64url hali (code_challenge)
        // Salesforce'a gonderilir. Verifier'in kendisi yalnizca Callback'te, token
        // degisiminde kullanilir - hic disari cikmaz.
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);

        HttpContext.Session.SetString(StateSessionKey, state);
        HttpContext.Session.SetString(VerifierSessionKey, codeVerifier);

        var redirectUri = await CallbackUrlAsync(ct);
        var authorizeUrl = await _salesforce.BuildAuthorizeUrlAsync(redirectUri, state, codeChallenge, ct);

        if (authorizeUrl is null)
        {
            TempData["SettingsError"] = "Önce Consumer Key / Consumer Secret'ı kaydedin.";
            return RedirectToAction("Index", "Settings");
        }

        return Redirect(authorizeUrl);
    }

    [HttpGet]
    public async Task<IActionResult> Callback(string? code, string? state, string? error, string? error_description, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            TempData["SettingsError"] = $"Salesforce bağlantısı reddedildi: {error_description ?? error}";
            return RedirectToAction("Index", "Settings");
        }

        var expectedState = HttpContext.Session.GetString(StateSessionKey);
        var codeVerifier = HttpContext.Session.GetString(VerifierSessionKey);
        HttpContext.Session.Remove(StateSessionKey);
        HttpContext.Session.Remove(VerifierSessionKey);

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) || state != expectedState
            || string.IsNullOrWhiteSpace(codeVerifier))
        {
            TempData["SettingsError"] = "Salesforce bağlantısı doğrulanamadı (geçersiz istek). Tekrar deneyin.";
            return RedirectToAction("Index", "Settings");
        }

        var (success, err) = await _salesforce.ExchangeAuthorizationCodeAsync(code, await CallbackUrlAsync(ct), codeVerifier, ct);
        if (!success)
        {
            TempData["SettingsError"] = $"Salesforce bağlantısı kurulamadı: {err}";
            return RedirectToAction("Index", "Settings");
        }

        // Tek tus kurulum: baglanti kurulur kurulmaz gerekli ozel alanlar olusturulur
        // ve kayit sayfalarina eklenir.
        ReportSchema(await _salesforce.EnsureSchemaAsync(ct), "Salesforce bağlantısı kuruldu.");
        return RedirectToAction("Index", "Settings");
    }

    /// <summary>Ayarlar'daki "Alanları kur / kontrol et" butonu.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetupSchema(CancellationToken ct)
    {
        ReportSchema(await _salesforce.EnsureSchemaAsync(ct), "Salesforce alanları kontrol edildi.");
        return RedirectToAction("Index", "Settings");
    }

    private void ReportSchema(SalesforceSchemaResult schema, string lead)
    {
        if (!schema.Success)
        {
            TempData["SettingsError"] = $"Salesforce alan kurulumu tamamlanamadı: {schema.Error}";
            return;
        }

        var parts = new List<string> { lead };
        if (schema.Created.Count > 0) parts.Add($"{schema.Created.Count} özel alan oluşturuldu.");
        if (schema.LayoutsUpdated.Count > 0) parts.Add($"Alanlar {schema.LayoutsUpdated.Count} kayıt sayfasına eklendi.");
        if (schema.Created.Count == 0 && schema.LayoutsUpdated.Count == 0) parts.Add("Her şey zaten hazır.");
        TempData["SettingsSaved"] = string.Join(" ", parts);

        // Duzen guncellenemese de aktarim calisir; kullanici bilsin diye ayrica gosterilir.
        if (schema.Warning is not null) TempData["SettingsError"] = schema.Warning;
    }

    /// <summary>RFC 7636: 43-128 karakter, [A-Z a-z 0-9 - . _ ~]. 32 rastgele byte -> base64url yeterli ve guvenli.</summary>
    private static string GenerateCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string ComputeCodeChallenge(string codeVerifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await _salesforce.DisconnectAsync(ct);
        TempData["SettingsSaved"] = "Salesforce bağlantısı kaldırıldı.";
        return RedirectToAction("Index", "Settings");
    }

    /// <summary>
    /// Salesforce'a gonderilen ve Connected App'te kayitli olmasi gereken geri donus adresi.
    /// Dis adres tanimliysa her zaman o kullanilir; degilse istegin geldigi adres
    /// (ngrok/reverse proxy arkasinda Program.cs'teki UseForwardedHeaders sayesinde dogru gelir).
    /// </summary>
    private async Task<string> CallbackUrlAsync(CancellationToken ct) =>
        $"{await PublicBaseUrlAsync(ct) ?? CurrentBaseUrl()}/Salesforce/Callback";

    private string CurrentBaseUrl() => $"{Request.Scheme}://{Request.Host}";

    private async Task<string?> PublicBaseUrlAsync(CancellationToken ct) =>
        NormalizeBaseUrl(await _settings.GetAsync(Models.SettingKeys.SalesforcePublicBaseUrl, ct));

    /// <summary>Gecerli mutlak http(s) adresini sondaki "/" olmadan dondurur; gecersizse null.</summary>
    public static string? NormalizeBaseUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
}
