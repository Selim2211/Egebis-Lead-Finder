using System.Security.Claims;
using System.Text.RegularExpressions;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services.Auth;

/// <summary>Kullanici islemlerinin sonucu: basarisizsa kullaniciya gosterilecek mesaj.</summary>
public readonly record struct UserResult(bool Ok, string? Error = null, AppUser? User = null)
{
    public static UserResult Fail(string error) => new(false, error);
    public static UserResult Success(AppUser user) => new(true, null, user);
}

public enum LoginOutcome { Success, InvalidCredentials, Locked, Inactive }

public readonly record struct LoginResult(LoginOutcome Outcome, AppUser? User = null, DateTime? LockedUntil = null);

/// <summary>
/// Kullanici yonetimi ve giris kontrolu. Sifreler PBKDF2 ile (ASP.NET Core PasswordHasher)
/// saklanir; 5 hatali giriste hesap 15 dakika kilitlenir. Son aktif yonetici silinemez,
/// pasiflestirilemez ve rolu dusurulemez.
/// </summary>
public class UserService
{
    public const int MinPasswordLength = 8;
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public const string StampClaim = "eg:stamp";
    public const string MustChangeClaim = "eg:mustchange";

    private static readonly Regex UserNamePattern = new("^[a-z0-9._-]{3,64}$", RegexOptions.CultureInvariant);

    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;

    public UserService(ApplicationDbContext db, IPasswordHasher<AppUser> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public static string NormalizeUserName(string? userName) =>
        (userName ?? string.Empty).Trim().ToLowerInvariant();

    public static string? ValidateUserName(string userName) =>
        UserNamePattern.IsMatch(userName)
            ? null
            : "Kullanıcı adı 3-64 karakter olmalı; yalnızca küçük harf, rakam, nokta, tire ve alt çizgi içerebilir.";

    public static string? ValidatePassword(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinPasswordLength
            ? $"Şifre en az {MinPasswordLength} karakter olmalı."
            : password.Length > 128 ? "Şifre en fazla 128 karakter olabilir." : null;

    public Task<bool> AnyUsersAsync(CancellationToken ct = default) => _db.Users.AnyAsync(ct);

    public Task<List<AppUser>> ListAsync(CancellationToken ct = default) =>
        _db.Users.AsNoTracking().OrderByDescending(u => u.Role).ThenBy(u => u.UserName).ToListAsync(ct);

    public Task<AppUser?> FindAsync(int id, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<LoginResult> LoginAsync(string? userName, string? password, CancellationToken ct = default)
    {
        var name = NormalizeUserName(userName);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == name, ct);

        if (user is null)
        {
            // Kullanici yoksa da ayni hash suresi harcanir: yanit suresi hangi adlarin var oldugunu ele vermesin.
            _hasher.HashPassword(new AppUser(), password ?? string.Empty);
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        var now = DateTime.UtcNow;
        if (user.LockedUntil is { } until && until > now)
            return new LoginResult(LoginOutcome.Locked, user, until);

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
        if (verify == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockedUntil = now + LockoutDuration;
                user.FailedLoginCount = 0;
                await _db.SaveChangesAsync(ct);
                return new LoginResult(LoginOutcome.Locked, user, user.LockedUntil);
            }

            await _db.SaveChangesAsync(ct);
            return new LoginResult(LoginOutcome.InvalidCredentials, user);
        }

        if (!user.IsActive)
            return new LoginResult(LoginOutcome.Inactive, user);

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = _hasher.HashPassword(user, password!);

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        await _db.SaveChangesAsync(ct);
        return new LoginResult(LoginOutcome.Success, user);
    }

    public async Task<UserResult> CreateAsync(string? userName, string? fullName, string? email, string? password,
        UserRole role, bool mustChangePassword, CancellationToken ct = default)
    {
        var name = NormalizeUserName(userName);
        if (ValidateUserName(name) is { } nameError) return UserResult.Fail(nameError);
        if (ValidatePassword(password) is { } passwordError) return UserResult.Fail(passwordError);
        if (await _db.Users.AnyAsync(u => u.UserName == name, ct))
            return UserResult.Fail($"\"{name}\" kullanıcı adı zaten kullanılıyor.");

        var user = new AppUser
        {
            UserName = name,
            FullName = Clean(fullName, 150),
            Email = Clean(email, 255),
            Role = role,
            MustChangePassword = mustChangePassword
        };
        user.PasswordHash = _hasher.HashPassword(user, password!);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return UserResult.Success(user);
    }

    public async Task<UserResult> UpdateAsync(int id, string? fullName, string? email, UserRole role, bool isActive,
        int actingUserId, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct);
        if (user is null) return UserResult.Fail("Kullanıcı bulunamadı.");

        var losesAdmin = user.Role == UserRole.Admin && user.IsActive && (role != UserRole.Admin || !isActive);
        if (losesAdmin && await IsLastActiveAdminAsync(user.Id, ct))
            return UserResult.Fail("Son aktif yönetici pasifleştirilemez veya rolü düşürülemez.");
        if (id == actingUserId && !isActive)
            return UserResult.Fail("Kendi hesabınızı pasifleştiremezsiniz.");

        var securityChanged = user.Role != role || user.IsActive != isActive;
        user.FullName = Clean(fullName, 150);
        user.Email = Clean(email, 255);
        user.Role = role;
        user.IsActive = isActive;
        if (securityChanged) user.SecurityStamp = NewStamp();

        await _db.SaveChangesAsync(ct);
        return UserResult.Success(user);
    }

    /// <summary>Yonetici sifre sifirlamasi: kullanicinin acik oturumlari kapanir, ilk giriste yeni sifre ister.</summary>
    public async Task<UserResult> ResetPasswordAsync(int id, string? newPassword, CancellationToken ct = default)
    {
        if (ValidatePassword(newPassword) is { } error) return UserResult.Fail(error);
        var user = await FindAsync(id, ct);
        if (user is null) return UserResult.Fail("Kullanıcı bulunamadı.");

        user.PasswordHash = _hasher.HashPassword(user, newPassword!);
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.SecurityStamp = NewStamp();
        await _db.SaveChangesAsync(ct);
        return UserResult.Success(user);
    }

    public async Task<UserResult> ChangeOwnPasswordAsync(int id, string? currentPassword, string? newPassword, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct);
        if (user is null) return UserResult.Fail("Kullanıcı bulunamadı.");

        if (_hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? string.Empty) == PasswordVerificationResult.Failed)
            return UserResult.Fail("Mevcut şifre yanlış.");
        if (ValidatePassword(newPassword) is { } error) return UserResult.Fail(error);
        if (newPassword == currentPassword) return UserResult.Fail("Yeni şifre eskisiyle aynı olamaz.");

        user.PasswordHash = _hasher.HashPassword(user, newPassword!);
        user.MustChangePassword = false;
        user.SecurityStamp = NewStamp();
        await _db.SaveChangesAsync(ct);
        return UserResult.Success(user);
    }

    public async Task<UserResult> DeleteAsync(int id, int actingUserId, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct);
        if (user is null) return UserResult.Fail("Kullanıcı bulunamadı.");
        if (id == actingUserId) return UserResult.Fail("Kendi hesabınızı silemezsiniz.");
        if (user.Role == UserRole.Admin && user.IsActive && await IsLastActiveAdminAsync(id, ct))
            return UserResult.Fail("Son aktif yönetici silinemez.");

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        return UserResult.Success(user);
    }

    private Task<bool> IsLastActiveAdminAsync(int id, CancellationToken ct) =>
        _db.Users.AllAsync(u => u.Id == id || u.Role != UserRole.Admin || !u.IsActive, ct);

    /// <summary>Oturum cerezine yazilan kimlik: id, ad, rol ve guvenlik damgasi.</summary>
    public static ClaimsPrincipal CreatePrincipal(AppUser user, string scheme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.GivenName, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(StampClaim, user.SecurityStamp)
        };
        if (user.MustChangePassword) claims.Add(new Claim(MustChangeClaim, "1"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }

    private static string NewStamp() => Guid.NewGuid().ToString("n");

    private static string? Clean(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length > max ? v[..max] : v;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int? UserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(nameof(UserRole.Admin));

    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.GivenName) ?? user.Identity?.Name ?? string.Empty;
}
