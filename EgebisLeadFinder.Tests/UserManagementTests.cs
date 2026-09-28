using System.Security.Claims;
using EgebisLeadFinder.Controllers;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Tests;

/// <summary>Madde 6-7: kullanici yonetimi, kilitleme, son yonetici korumasi, yetki ve audit.</summary>
public class UserManagementTests
{
    private static (UserService Users, ApplicationDbContext Db) Create()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new UserService(db, new PasswordHasher<AppUser>()), db);
    }

    [Fact]
    public async Task Sifre_hashlenir_ve_dogru_sifreyle_giris_yapilir()
    {
        var (users, db) = Create();
        var created = await users.CreateAsync("Selim", "Selim A.", null, "cokgizli123", UserRole.Admin, false);

        Assert.True(created.Ok);
        var stored = await db.Users.SingleAsync();
        Assert.Equal("selim", stored.UserName);
        Assert.DoesNotContain("cokgizli123", stored.PasswordHash);

        Assert.Equal(LoginOutcome.Success, (await users.LoginAsync("SELIM", "cokgizli123")).Outcome);
        Assert.Equal(LoginOutcome.InvalidCredentials, (await users.LoginAsync("selim", "yanlis-sifre")).Outcome);
        Assert.Equal(LoginOutcome.InvalidCredentials, (await users.LoginAsync("olmayan", "cokgizli123")).Outcome);
    }

    [Fact]
    public async Task Bes_hatali_giriste_hesap_kilitlenir()
    {
        var (users, _) = Create();
        await users.CreateAsync("ahmet", null, null, "dogrusifre1", UserRole.User, false);

        for (var i = 0; i < UserService.MaxFailedLogins - 1; i++)
            Assert.Equal(LoginOutcome.InvalidCredentials, (await users.LoginAsync("ahmet", "yanlis")).Outcome);

        Assert.Equal(LoginOutcome.Locked, (await users.LoginAsync("ahmet", "yanlis")).Outcome);
        // Kilitliyken dogru sifre de reddedilir.
        Assert.Equal(LoginOutcome.Locked, (await users.LoginAsync("ahmet", "dogrusifre1")).Outcome);
    }

    [Fact]
    public async Task Pasif_kullanici_giris_yapamaz()
    {
        var (users, _) = Create();
        var admin = (await users.CreateAsync("admin", null, null, "adminsifre1", UserRole.Admin, false)).User!;
        var user = (await users.CreateAsync("ayse", null, null, "aysesifre1", UserRole.User, false)).User!;

        Assert.True((await users.UpdateAsync(user.Id, null, null, UserRole.User, isActive: false, admin.Id)).Ok);
        Assert.Equal(LoginOutcome.Inactive, (await users.LoginAsync("ayse", "aysesifre1")).Outcome);
    }

    [Fact]
    public async Task Son_aktif_yonetici_korunur()
    {
        var (users, _) = Create();
        var admin = (await users.CreateAsync("admin", null, null, "adminsifre1", UserRole.Admin, false)).User!;
        var other = (await users.CreateAsync("mehmet", null, null, "mehmetsifre", UserRole.User, false)).User!;

        Assert.False((await users.UpdateAsync(admin.Id, null, null, UserRole.User, true, other.Id)).Ok);
        Assert.False((await users.UpdateAsync(admin.Id, null, null, UserRole.Admin, false, other.Id)).Ok);
        Assert.False((await users.DeleteAsync(admin.Id, other.Id)).Ok);
        Assert.False((await users.DeleteAsync(admin.Id, admin.Id)).Ok);

        // Ikinci yonetici varken birinin rolu dusurulebilir.
        await users.UpdateAsync(other.Id, null, null, UserRole.Admin, true, admin.Id);
        Assert.True((await users.UpdateAsync(admin.Id, null, null, UserRole.User, true, other.Id)).Ok);
    }

    [Fact]
    public async Task Sifre_sifirlaninca_eski_oturum_gecersiz_ve_yeni_sifre_istenir()
    {
        var (users, _) = Create();
        var user = (await users.CreateAsync("zeynep", null, null, "ilksifre12", UserRole.User, false)).User!;
        var stampBefore = user.SecurityStamp;

        var reset = await users.ResetPasswordAsync(user.Id, "gecici1234");

        Assert.True(reset.Ok);
        Assert.NotEqual(stampBefore, reset.User!.SecurityStamp);
        Assert.True(reset.User.MustChangePassword);

        var principal = UserService.CreatePrincipal(reset.User, "test");
        Assert.True(principal.HasClaim(UserService.MustChangeClaim, "1"));

        var changed = await users.ChangeOwnPasswordAsync(user.Id, "gecici1234", "yenisifre99");
        Assert.True(changed.Ok);
        Assert.False(changed.User!.MustChangePassword);
    }

    [Theory]
    [InlineData("ab", false)]
    [InlineData("selim.akinci", true)]
    [InlineData("Selim", false)]
    [InlineData("user name", false)]
    public void Kullanici_adi_kurali(string name, bool valid) =>
        Assert.Equal(valid, UserService.ValidateUserName(name) is null);

    [Fact]
    public void Kisa_sifre_reddedilir()
    {
        Assert.NotNull(UserService.ValidatePassword("1234567"));
        Assert.Null(UserService.ValidatePassword("12345678"));
    }

    [Fact]
    public void Principal_rol_ve_damga_tasir()
    {
        var user = new AppUser { Id = 7, UserName = "admin", Role = UserRole.Admin };
        var principal = UserService.CreatePrincipal(user, "test");

        Assert.Equal(7, principal.UserId());
        Assert.True(principal.IsAdmin());
        Assert.Equal(user.SecurityStamp, principal.FindFirstValue(UserService.StampClaim));
    }

    [Theory]
    [InlineData(typeof(SettingsController))]
    [InlineData(typeof(IcpController))]
    [InlineData(typeof(UsersController))]
    [InlineData(typeof(AuditController))]
    [InlineData(typeof(SalesforceController))]
    public void Yonetici_ekranlari_admin_rolu_ister(Type controller)
    {
        var attr = controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal(nameof(UserRole.Admin), attr.Roles);
    }

    [Theory]
    [InlineData(typeof(CompanyController), nameof(CompanyController.DeleteAll))]
    [InlineData(typeof(SequenceController), nameof(SequenceController.SaveSettings))]
    [InlineData(typeof(SequenceController), nameof(SequenceController.TestImap))]
    public void Yikici_ve_ayar_islemleri_admin_ister(Type controller, string action)
    {
        var method = controller.GetMethods().First(m => m.Name == action);
        var attr = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal(nameof(UserRole.Admin), attr.Roles);
    }

    [Fact]
    public void Audit_etiketleri_tekil_ve_okunur()
    {
        var options = AuditController.ActionOptions();
        Assert.Equal(options.Count, options.Select(o => o.Code).Distinct().Count());
        Assert.Equal("Giriş", AuditController.ActionLabel("login"));
        Assert.Equal("Tüm firmalar silindi", AuditController.ActionLabel("company.deleteall"));
        Assert.Equal("bilinmeyen.islem", AuditController.ActionLabel("bilinmeyen.islem"));
    }
}
