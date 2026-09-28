using EgebisLeadFinder.Services;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>
/// API Kullanimi: hangi anahtar ne kadar kredi/cagri kullandi, hangisinin kotasi dolmak uzere.
/// Tum kullanicilar gorebilir; limitler ve anahtarlar Ayarlar'dan (yonetici) degisir.
/// </summary>
public class UsageController : Controller
{
    private readonly UsageService _usage;

    public UsageController(UsageService usage) => _usage = usage;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await _usage.GetAllAsync(ct));

    /// <summary>Apollo'nun canli kredi durumunu sorgular (bakiye API'si olan tek saglayici).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var status = await _usage.RefreshApolloAsync(ct);
        TempData[status.Ok ? "SettingsSaved" : "SettingsError"] = status.Ok
            ? $"Apollo kredi durumu güncellendi: {status.LeftOver} kredi kaldı."
            : $"Apollo kredi durumu alınamadı: {status.Error} Yerel sayaç gösteriliyor.";
        return RedirectToAction(nameof(Index));
    }
}
