using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>Giris, cikis, ilk kurulum (ilk yonetici) ve kendi sifresini degistirme.</summary>
public class AccountController : Controller
{
    private readonly UserService _users;
    private readonly IAuditLogger _audit;

    public AccountController(UserService users, IAuditLogger audit)
    {
        _users = users;
        _audit = audit;
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(returnUrl);
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        var result = await _users.LoginAsync(model.UserName, model.Password, ct);
        var actor = AuditActor.From(HttpContext, result.User) with
        {
            UserName = result.User?.UserName ?? UserService.NormalizeUserName(model.UserName)
        };

        switch (result.Outcome)
        {
            case LoginOutcome.Success:
                await SignInAsync(result.User!, model.RememberMe);
                await _audit.LogAsync("login", "Giriş yapıldı", "User", result.User!.Id.ToString(), actor: actor, ct: ct);
                return result.User!.MustChangePassword
                    ? RedirectToAction(nameof(ChangePassword))
                    : RedirectToLocal(model.ReturnUrl);

            case LoginOutcome.Locked:
                await _audit.LogAsync("login.locked", "Hesap kilitli; giriş reddedildi", "User", result.User?.Id.ToString(),
                    success: false, actor: actor, ct: ct);
                model.Error = $"Çok fazla hatalı deneme. Hesap {result.LockedUntil!.Value.ToLocalTime():HH:mm}'e kadar kilitli.";
                break;

            case LoginOutcome.Inactive:
                await _audit.LogAsync("login.failed", "Pasif hesapla giriş denendi", "User", result.User?.Id.ToString(),
                    success: false, actor: actor, ct: ct);
                model.Error = "Bu hesap pasif. Yöneticinize başvurun.";
                break;

            default:
                await _audit.LogAsync("login.failed", "Hatalı kullanıcı adı veya şifre", "User", result.User?.Id.ToString(),
                    success: false, actor: actor, ct: ct);
                model.Error = "Kullanıcı adı veya şifre hatalı.";
                break;
        }

        model.Password = null;
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _audit.LogAsync("logout", "Çıkış yapıldı", "User", User.UserId()?.ToString(), ct: ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    /// <summary>Hic kullanici yokken ilk yonetici hesabi olusturulur; sonra bu ekran kapanir.</summary>
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Setup(CancellationToken ct)
    {
        if (await _users.AnyUsersAsync(ct)) return RedirectToAction(nameof(Login));
        return View(new SetupViewModel());
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Setup(SetupViewModel model, CancellationToken ct)
    {
        if (await _users.AnyUsersAsync(ct)) return RedirectToAction(nameof(Login));

        if (model.Password != model.ConfirmPassword)
        {
            model.Error = "Şifreler eşleşmiyor.";
            return View(model);
        }

        var result = await _users.CreateAsync(model.UserName, model.FullName, model.Email, model.Password,
            UserRole.Admin, mustChangePassword: false, ct);
        if (!result.Ok)
        {
            model.Error = result.Error;
            return View(model);
        }

        SetupState.HasUsers = true;
        // Normal giris yolundan gecsin: son giris zamani da yazilir.
        var login = await _users.LoginAsync(model.UserName, model.Password, ct);
        await SignInAsync(login.User ?? result.User!, remember: false);
        await _audit.LogAsync("user.setup", $"İlk yönetici oluşturuldu: {result.User!.UserName}", "User",
            result.User.Id.ToString(), actor: AuditActor.From(HttpContext, result.User), ct: ct);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ChangePassword() =>
        View(new ChangePasswordViewModel { Forced = User.HasClaim(UserService.MustChangeClaim, "1") });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken ct)
    {
        model.Forced = User.HasClaim(UserService.MustChangeClaim, "1");
        if (model.NewPassword != model.ConfirmPassword)
        {
            model.Error = "Yeni şifreler eşleşmiyor.";
            return View(model);
        }

        var result = await _users.ChangeOwnPasswordAsync(User.UserId() ?? 0, model.CurrentPassword, model.NewPassword, ct);
        await _audit.LogAsync("user.changepassword", result.Ok ? "Kendi şifresini değiştirdi" : "Şifre değiştirme başarısız",
            "User", User.UserId()?.ToString(), success: result.Ok, ct: ct);

        if (!result.Ok)
        {
            model.Error = result.Error;
            return View(model);
        }

        // Guvenlik damgasi degisti: yeni damgayla oturum yenilenir.
        await SignInAsync(result.User!, remember: false);
        TempData["SettingsSaved"] = "Şifreniz değiştirildi.";
        return RedirectToAction("Index", "Home");
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Denied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    private Task SignInAsync(AppUser user, bool remember) =>
        HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            UserService.CreatePrincipal(user, CookieAuthenticationDefaults.AuthenticationScheme),
            new AuthenticationProperties
            {
                IsPersistent = remember,
                ExpiresUtc = remember ? DateTimeOffset.UtcNow.AddDays(7) : null
            });

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");
}
