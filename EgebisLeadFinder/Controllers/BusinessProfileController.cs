using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EgebisLeadFinder.Controllers;

/// <summary>
/// "Biz ne arıyoruz?" — sirket profili ve hedef segmentler. Herkes gorur, yalniz yonetici degistirir.
/// </summary>
public class BusinessProfileController : Controller
{
    private readonly BusinessProfileService _profiles;
    private readonly ProfileSignalService _signals;
    private readonly ISettingsService _settings;
    private readonly ProfileTemplateService _templates;

    public BusinessProfileController(BusinessProfileService profiles, ISettingsService settings, ProfileTemplateService templates, ProfileSignalService signals)
    {
        _profiles = profiles;
        _signals = signals;
        _settings = settings;
        _templates = templates;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var profile = await _profiles.GetAsync(ct);
        return View(BusinessProfileForm.From(profile, await _settings.GetTitleKeywordsAsync(ct)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Index(BusinessProfileForm form, CancellationToken ct)
    {
        var profile = form.ToProfile();
        if (string.IsNullOrWhiteSpace(profile.Offering) || string.IsNullOrWhiteSpace(profile.IdealCustomer))
        {
            ModelState.AddModelError(string.Empty, "\"Ne satıyoruz?\" ve \"İdeal müşterimiz\" alanları zorunlu; yapay zekâ firmaları bunlara göre değerlendirir.");
            return View(form);
        }

        await _profiles.SaveAsync(profile, IcpController.SplitList(form.TargetTitles), User.Identity?.Name, ct);

        var message = $"Şirket profili kaydedildi ({profile.Segments.Count} segment). Firma araması, yapay zekâ analizi, araştırma ve mail önerileri artık bu tanıma göre çalışır. " +
                      "Daha önce bulunan firmaları güncellemek için firma detayında “Yeniden Analiz Et” ve “Araştır”ı kullanın; mail taslaklarını Taslak Düzenleyici'den profilinize göre oluşturabilirsiniz.";
        if (form.ApplyToIcp)
        {
            var added = await _profiles.ApplySegmentsToIcpAsync(profile, ct);
            message += added > 0 ? $" ICP'ye {added} yeni kriter eklendi." : " ICP zaten güncel.";
        }

        // Puanlama anahtar kelimeleri bos birakildiysa yapay zeka tanimdan uretir (elle girilenlere dokunulmaz).
        var generated = await _signals.EnsureAsync(User.Identity?.Name, ct);
        if (generated.Created > 0)
            message += $" Yapay zekâ {generated.Created} puanlama anahtar kelimesi üretti (aşağıdan düzenleyebilirsiniz).";
        else if (generated.Error is not null)
            message += $" Puanlama anahtar kelimeleri şimdi üretilemedi ({generated.Error}); \"Yapay zekâ ile öner\" ile yeniden deneyebilirsiniz.";

        // E-posta taslaklari profile gore otomatik hazirlanir (yalnizca eksikler; duzenlenenlere dokunulmaz).
        // Basarisiz olursa (anahtar yok, kota) profil yine kaydedilmistir; kullanici Taslak Duzenleyici'den yeniden dener.
        var provision = await _templates.ProvisionAsync(await _profiles.GetAsync(ct), onlyMissing: true, deactivateAll: false, ct);
        if (provision.Created > 0)
            message += $" Profilinize göre {provision.Created} e-posta taslağı otomatik hazırlandı (Taslak Düzenleyici); göndermeden önce kontrol edin.";
        else if (!provision.Success)
            message += $" E-posta taslakları şimdi hazırlanamadı ({provision.Error}); Taslak Düzenleyici'den “Profilime göre oluştur”a basarak yeniden deneyebilirsiniz.";

        AuditActionFilter.SetAuditSummary(HttpContext,
            $"Şirket profili kaydedildi: {profile.DisplayName}, {profile.Segments.Count} segment" + (form.ApplyToIcp ? ", ICP'ye aktarıldı" : ""));
        TempData["ProfileSaved"] = message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Formdaki mevcut tanima gore puanlama anahtar kelimelerini yapay zekaya onerttirir (kaydetmez).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> SuggestSignals(BusinessProfileForm form, [FromServices] IScoringSignalAi ai, CancellationToken ct)
    {
        var profile = form.ToProfile();
        if (!profile.IsConfigured)
            return Json(new { ok = false, error = "Önce \"Ne satıyoruz?\" ve \"İdeal müşterimiz\" alanlarını doldurun." });

        var result = await ai.SuggestSignalsAsync(profile, ct);
        if (!result.Success)
        {
            AuditActionFilter.MarkFailed(HttpContext);
            return Json(new { ok = false, error = result.Error });
        }

        AuditActionFilter.SetAuditSummary(HttpContext, $"Puanlama anahtar kelimeleri önerildi: {result.Signals.Count}");
        return Json(new { ok = true, lines = ScoringSignal.FormatLines(result.Signals) });
    }

    /// <summary>Sirket sitesinden yapay zeka taslagi (kaydetmez; form doldurulur, kullanici duzeltir).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Draft(string? website, CancellationToken ct)
    {
        var draft = await _profiles.DraftFromWebsiteAsync(website ?? string.Empty, ct);
        if (!draft.Success)
        {
            AuditActionFilter.MarkFailed(HttpContext);
            return Json(new { ok = false, error = draft.Error });
        }

        AuditActionFilter.SetAuditSummary(HttpContext, $"Şirket profili taslağı çıkarıldı: {draft.Profile.Website}");
        var p = draft.Profile;
        return Json(new
        {
            ok = true,
            profile = new
            {
                p.CompanyName,
                p.Website,
                p.Offering,
                problems = p.ProblemsWeSolve,
                p.IdealCustomer,
                p.NotCustomers,
                p.Competitors,
                exampleCustomers = string.Join(", ", p.ExampleCustomers),
                customerKind = p.CustomerKind,
                buyingSignals = string.Join(", ", p.BuyingSignals),
                scoringSignals = ScoringSignal.FormatLines(p.ScoringSignals),
                segments = p.Segments.Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Description,
                    s.SearchTerm,
                    region = s.RegionKey,
                    keywords = string.Join(", ", s.Keywords),
                    nace = string.Join(", ", s.NaceCodes),
                    exclude = string.Join(", ", s.ExcludeKeywords),
                    titles = string.Join(", ", s.TargetTitles)
                })
            },
            titles = string.Join(", ", draft.TargetTitles)
        });
    }
}
