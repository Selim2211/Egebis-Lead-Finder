using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EgebisLeadFinder.Services.Auth;

/// <summary>
/// Tum degistirici islemleri (POST) ve disa aktarmalari denetim kaydina yazar. Boylece yeni bir
/// ekran eklendiginde de "tum aktiviteler" eksiksiz kalir. Action, daha anlamli bir ozet yazmak
/// isterse <see cref="SetAuditSummary"/> ile HttpContext.Items'a birakir.
/// </summary>
public class AuditActionFilter : IAsyncActionFilter
{
    public const string SummaryKey = "eg:audit-summary";
    public const string SkipKey = "eg:audit-skip";
    public const string FailedKey = "eg:audit-failed";

    /// <summary>Action islemi yapamadi (dogrulama hatasi vb.) ama yonlendirme ile dondu: kayit basarisiz yazilsin.</summary>
    public static void MarkFailed(HttpContext http) => http.Items[FailedKey] = true;

    /// <summary>Controller.Action → (islem kodu, Turkce etiket). Listede olmayanlar controller.action olarak yazilir.</summary>
    public static readonly IReadOnlyDictionary<string, (string Code, string Label)> Known =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Search"] = ("search.start", "Firma araması"),
            ["Company.StartSearch"] = ("search.start", "Firma araması başlatıldı"),
            ["Company.CancelJob"] = ("job.cancel", "Arka plan işi iptal edildi"),
            ["Company.SaveProfile"] = ("profile.save", "Arama profili kaydedildi"),
            ["Company.DeleteProfile"] = ("profile.delete", "Arama profili silindi"),
            ["Company.SaveToProfile"] = ("profile.addcompanies", "Firmalar profile eklendi"),
            ["Company.StartResearch"] = ("company.research", "Firma analizi başlatıldı"),
            ["Company.Research"] = ("company.research", "Firma analizi"),
            ["Company.ReanalyzeAi"] = ("company.reanalyze", "Firma yeniden analiz edildi"),
            ["Company.ToggleStage"] = ("company.stage", "Firma aşaması değişti"),
            ["Company.DeleteCompany"] = ("company.delete", "Firma silindi"),
            ["Company.DeleteAll"] = ("company.deleteall", "Tüm firmalar silindi"),
            ["Company.SyncToSalesforce"] = ("salesforce.sync", "Firma Salesforce'a gönderildi"),
            ["Company.Enrich"] = ("company.enrich", "Karar vericiler arandı"),
            ["Company.RevealContactEmail"] = ("contact.reveal", "Kişi e-postası açıldı (kredi)"),
            ["Company.CreateLead"] = ("lead.create", "Lead oluşturuldu"),
            ["Company.Export"] = ("export.companies", "Firmalar dışa aktarıldı"),
            ["Email.Send"] = ("email.send", "E-posta gönderildi"),
            ["Email.MarkSent"] = ("email.marksent", "E-posta gönderildi olarak işaretlendi"),
            ["Email.Draft"] = ("email.aidraft", "AI ile e-posta taslağı yazıldı"),
            ["Email.UploadImage"] = ("email.image", "E-posta görseli yüklendi"),
            ["Email.RemoveFromLibrary"] = ("email.image.delete", "E-posta görseli kaldırıldı"),
            ["Lead.UpdateStatus"] = ("lead.status", "Lead durumu değişti"),
            ["Lead.Toggle"] = ("lead.stage", "Lead işareti değişti"),
            ["Lead.MarkReplied"] = ("lead.replied", "Cevap geldi işaretlendi"),
            ["Lead.Snooze"] = ("lead.snooze", "Lead ertelendi"),
            ["Lead.RevealEmail"] = ("contact.reveal", "Kişi e-postası açıldı (kredi)"),
            ["Lead.SaveNotes"] = ("lead.notes", "Lead notu kaydedildi"),
            ["Lead.SyncToSalesforce"] = ("salesforce.sync", "Lead Salesforce'a gönderildi"),
            ["Lead.Delete"] = ("lead.delete", "Lead silindi"),
            ["Lead.Export"] = ("export.leads", "Lead'ler dışa aktarıldı"),
            ["Sequence.Create"] = ("sequence.create", "Mail dizisi oluşturuldu"),
            ["Sequence.SaveSteps"] = ("sequence.update", "Mail dizisi güncellendi"),
            ["Sequence.Delete"] = ("sequence.delete", "Mail dizisi pasife alındı"),
            ["Sequence.Enroll"] = ("sequence.enroll", "Lead'ler diziye eklendi"),
            ["Sequence.Stop"] = ("sequence.stop", "Lead dizisi durduruldu"),
            ["Sequence.SaveSettings"] = ("settings.sequence", "Dizi/IMAP ayarları değişti"),
            ["Sequence.TestImap"] = ("settings.imaptest", "IMAP bağlantısı test edildi"),
            ["Sequence.CheckNow"] = ("sequence.checkinbox", "Gelen kutusu kontrol edildi"),
            ["Settings.Index"] = ("settings.update", "Ayarlar değişti"),
            ["Settings.SendTestEmail"] = ("settings.testmail", "Test e-postası gönderildi"),
            ["Settings.TestSalesforceConnection"] = ("salesforce.test", "Salesforce bağlantısı test edildi"),
            ["Settings.ResetSerperUsage"] = ("usage.reset", "API sayacı sıfırlandı"),
            ["Salesforce.SetupSchema"] = ("salesforce.schema", "Salesforce alanları kuruldu"),
            ["Salesforce.Disconnect"] = ("salesforce.disconnect", "Salesforce bağlantısı kesildi"),
            ["Icp.Index"] = ("settings.icp", "ICP profili kaydedildi"),
            ["Company.UpdateAddress"] = ("company.address", "Firma adresi güncellendi"),
            ["Icp.StartFillNace"] = ("icp.fillnace", "NACE kodu doldurma başlatıldı"),
            ["BusinessProfile.Index"] = ("settings.businessprofile", "Şirket profili (Biz ne arıyoruz?) kaydedildi"),
            ["BusinessProfile.Draft"] = ("businessprofile.draft", "Şirket profili taslağı siteden çıkarıldı"),
            ["Template.Save"] = ("template.save", "E-posta taslağı kaydedildi"),
            ["Template.Duplicate"] = ("template.duplicate", "E-posta taslağı kopyalandı"),
            ["Template.Delete"] = ("template.delete", "E-posta taslağı silindi"),
            ["Template.GenerateFromProfile"] = ("template.generate", "Profilden e-posta taslakları oluşturuldu"),
            ["Users.Create"] = ("user.create", "Kullanıcı oluşturuldu"),
            ["Users.Update"] = ("user.update", "Kullanıcı güncellendi"),
            ["Users.ResetPassword"] = ("user.resetpassword", "Kullanıcı şifresi sıfırlandı"),
            ["Users.Delete"] = ("user.delete", "Kullanıcı silindi"),
            ["Audit.Export"] = ("export.audit", "Audit kayıtları dışa aktarıldı"),
            ["Usage.Refresh"] = ("usage.refresh", "API bakiyeleri yenilendi"),
            ["Company.SetProfileVisibility"] = ("profile.visibility", "Arama şablonu paylaşımı değişti"),
            ["Favorites.Toggle"] = ("favorite.toggle", "Favori firma eklendi/çıkarıldı"),
            ["Favorites.Visibility"] = ("favorite.visibility", "Favori listesi paylaşımı değişti"),
            ["Favorites.Export"] = ("export.favorites", "Favori firmalar dışa aktarıldı"),
            ["Company.CompareAi"] = ("company.compare.ai", "Firma karşılaştırma yapay zekâ yorumu"),
            ["Company.CompareExport"] = ("export.compare", "Firma karşılaştırma raporu dışa aktarıldı"),
            ["Sector.Analyze"] = ("sector.analyze", "Sektör analizi yapıldı"),
            ["Sector.Export"] = ("export.sector", "Sektör analizi raporu dışa aktarıldı"),
            ["Sector.Delete"] = ("sector.delete", "Sektör analizi raporu silindi"),
            ["Icp.Suggest"] = ("icp.suggest", "Siteden ICP önerisi alındı"),
            ["Icp.DismissSuggestion"] = ("icp.suggest.dismiss", "ICP önerisi kapatıldı"),
            ["Icp.StartSuggest"] = ("icp.suggest", "Siteden ICP önerisi istendi"),
            ["Sector.StartAnalyze"] = ("sector.analyze", "Sektör analizi başlatıldı"),
            ["Company.StartCompareAi"] = ("company.compare.ai", "Firma karşılaştırma yapay zekâ yorumu"),
        };

    private static readonly string[] EntityKeys = { "id", "leadId", "companyId", "profileId", "sequenceId", "userId" };

    private readonly IAuditLogger _audit;

    public AuditActionFilter(IAuditLogger audit) => _audit = audit;

    public static void SetAuditSummary(HttpContext http, string summary) => http.Items[SummaryKey] = summary;

    public static void SkipAudit(HttpContext http) => http.Items[SkipKey] = true;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
        var action = context.RouteData.Values["action"]?.ToString() ?? "";
        var key = $"{controller}.{action}";
        var method = context.HttpContext.Request.Method;

        // Giris/cikis AccountController'da acikca yazilir; GET'ler (Export disinda) okuma islemidir.
        var track = !controller.Equals("Account", StringComparison.OrdinalIgnoreCase)
            && (HttpMethods.IsPost(method) || action.Equals("Export", StringComparison.OrdinalIgnoreCase));

        var executed = await next();
        if (!track || context.HttpContext.Items.ContainsKey(SkipKey)) return;

        var (code, label) = Known.TryGetValue(key, out var known) ? known : (key.ToLowerInvariant(), key);
        var entityId = EntityKeys
            .Select(k => context.ActionArguments.TryGetValue(k, out var v) ? v?.ToString() : null)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var success = executed.Exception is null || executed.ExceptionHandled;
        if (executed.Result is IStatusCodeActionResult { StatusCode: >= 400 }) success = false;
        if (context.HttpContext.Items.ContainsKey(FailedKey)) success = false;

        var summary = context.HttpContext.Items.TryGetValue(SummaryKey, out var s) && s is string text
            ? text
            : entityId is not null ? $"{label} (#{entityId})" : label;
        if (executed.Exception is not null && !executed.ExceptionHandled)
            summary += $" — hata: {executed.Exception.Message}";

        await _audit.LogAsync(code, summary, controller, entityId, success);
    }
}
