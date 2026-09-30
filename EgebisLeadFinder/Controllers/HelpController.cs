using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>Kullanici kilavuzu: ekran goruntulu, adim adim anlatim (her sayfada ayrica "sayfa turu" var).</summary>
public class HelpController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
