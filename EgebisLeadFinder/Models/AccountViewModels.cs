namespace EgebisLeadFinder.Models;

public class LoginViewModel
{
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
}

public class SetupViewModel
{
    public string? UserName { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? ConfirmPassword { get; set; }
    public string? Error { get; set; }
}

public class ChangePasswordViewModel
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }

    /// <summary>Yonetici sifreyi sifirladi; kullanici yeni sifre belirlemeden devam edemez.</summary>
    public bool Forced { get; set; }
    public string? Error { get; set; }
}

public class UsersViewModel
{
    public List<AppUser> Users { get; set; } = new();
    public int CurrentUserId { get; set; }

    /// <summary>Kullanici bazinda son giris kayitlari (audit log'dan).</summary>
    public Dictionary<int, List<AuditLog>> RecentLogins { get; set; } = new();
}

public class AuditListViewModel
{
    public List<AuditLog> Entries { get; set; } = new();
    public PagerModel Pager { get; set; } = new();

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? UserId { get; set; }
    public string? Action { get; set; }
    public string? Q { get; set; }
    public bool OnlyFailed { get; set; }

    public List<AppUser> Users { get; set; } = new();

    /// <summary>Filtre listesi: islem kodu → etiket.</summary>
    public List<(string Code, string Label)> Actions { get; set; } = new();
}
