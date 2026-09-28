using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

/// <summary>Kullanici yonetimi (yalnizca yonetici): olustur, duzenle, sifre sifirla, sil.</summary>
[Authorize(Roles = nameof(UserRole.Admin))]
public class UsersController : Controller
{
    private readonly UserService _users;
    private readonly ApplicationDbContext _db;

    public UsersController(UserService users, ApplicationDbContext db)
    {
        _users = users;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var users = await _users.ListAsync(ct);
        var logins = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "login" && a.UserId != null)
            .OrderByDescending(a => a.At)
            .Take(500)
            .ToListAsync(ct);

        return View(new UsersViewModel
        {
            Users = users,
            CurrentUserId = User.UserId() ?? 0,
            RecentLogins = logins.GroupBy(a => a.UserId!.Value).ToDictionary(g => g.Key, g => g.Take(5).ToList())
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string? userName, string? fullName, string? email, string? password,
        UserRole role, CancellationToken ct)
    {
        // Yonetici gecici sifre verir; kullanici ilk giriste kendi sifresini belirler.
        var result = await _users.CreateAsync(userName, fullName, email, password, role, mustChangePassword: true, ct);
        Report(result, $"\"{result.User?.UserName}\" oluşturuldu ({RoleLabel(role)}). İlk girişte şifresini değiştirmesi istenecek.",
            $"Kullanıcı oluşturuldu: {result.User?.UserName} ({RoleLabel(role)})");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int id, string? fullName, string? email, UserRole role, bool isActive, CancellationToken ct)
    {
        var result = await _users.UpdateAsync(id, fullName, email, role, isActive, User.UserId() ?? 0, ct);
        Report(result, $"\"{result.User?.UserName}\" güncellendi.",
            $"Kullanıcı güncellendi: {result.User?.UserName} — rol {RoleLabel(role)}, {(isActive ? "aktif" : "pasif")}");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id, string? newPassword, CancellationToken ct)
    {
        var result = await _users.ResetPasswordAsync(id, newPassword, ct);
        Report(result, $"\"{result.User?.UserName}\" şifresi sıfırlandı; açık oturumları kapatıldı.",
            $"Şifre sıfırlandı: {result.User?.UserName}");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await _users.DeleteAsync(id, User.UserId() ?? 0, ct);
        Report(result, $"\"{result.User?.UserName}\" silindi.", $"Kullanıcı silindi: {result.User?.UserName}");
        return RedirectToAction(nameof(Index));
    }

    private void Report(UserResult result, string okMessage, string auditSummary)
    {
        if (result.Ok)
        {
            TempData["SettingsSaved"] = okMessage;
            AuditActionFilter.SetAuditSummary(HttpContext, auditSummary);
        }
        else
        {
            TempData["SettingsError"] = result.Error;
            AuditActionFilter.SetAuditSummary(HttpContext, $"Başarısız: {result.Error}");
            AuditActionFilter.MarkFailed(HttpContext);
        }
    }

    public static string RoleLabel(UserRole role) => role == UserRole.Admin ? "Yönetici" : "Kullanıcı";
}
