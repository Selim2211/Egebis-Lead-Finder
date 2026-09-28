namespace EgebisLeadFinder.Services.Auth;

/// <summary>
/// Ilk kurulum tamamlandi mi (en az bir kullanici var mi)? Her istekte veritabanina
/// sormamak icin bir kez "var" gorulunce bellekte tutulur; kullanici silinip sifira inemez
/// (son yonetici silinemez).
/// </summary>
public static class SetupState
{
    private static volatile bool _hasUsers;

    public static bool HasUsers
    {
        get => _hasUsers;
        set => _hasUsers = value;
    }
}
