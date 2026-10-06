using EgebisLeadFinder.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>Arayüz dilini (Türkçe / İngilizce) cookie olarak saklar. Giriş ekranında da çalışır.</summary>
[AllowAnonymous]
public class LanguageController : Controller
{
    [HttpGet]
    public IActionResult Set(string? lang, string? returnUrl)
    {
        Response.Cookies.Append(Loc.CookieName, Loc.NormalizeCode(lang), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        });

        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");
    }
}
