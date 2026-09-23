using EgebisLeadFinder.Controllers;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Services;

public record RevealOutcome(bool Success, string Message, bool IsError = false);

/// <summary>
/// Tek kisinin e-postasini Apollo people/match ile acar (KREDI HARCAR). Firma detayi
/// ve lead karti ayni mantigi kullanir. Acilan profil bilgisi (calisma suresi,
/// konum, baslik) kisiye yazilir; firma puani yeni e-postayla yeniden hesaplanir.
/// </summary>
public class ContactRevealService
{
    private readonly ApplicationDbContext _db;
    private readonly IPersonEmailFinder _emailFinder;
    private readonly LeadScoringService _scoring;
    private readonly ILogger<ContactRevealService> _logger;

    public ContactRevealService(
        ApplicationDbContext db,
        IPersonEmailFinder emailFinder,
        LeadScoringService scoring,
        ILogger<ContactRevealService> logger)
    {
        _db = db;
        _emailFinder = emailFinder;
        _scoring = scoring;
        _logger = logger;
    }

    public async Task<RevealOutcome> RevealAsync(int contactId, CancellationToken ct)
    {
        var contact = await _db.Contacts
            .Include(c => c.Company!).ThenInclude(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == contactId, ct);

        if (contact?.Company is null)
            return new RevealOutcome(false, "Kişi bulunamadı.", IsError: true);

        if (!string.IsNullOrWhiteSpace(contact.Email))
            return new RevealOutcome(true, $"{contact.Name} için e-posta zaten açık: {contact.Email}");

        if (string.IsNullOrWhiteSpace(contact.ApolloId))
            return new RevealOutcome(false, "Bu kişi için Apollo kimliği yok, e-posta açılamaz.", IsError: true);

        try
        {
            var match = await _emailFinder.MatchByIdAsync(contact.ApolloId, ct);
            if (!match.Found)
                return new RevealOutcome(false, match.Error ?? "Bu kişi için e-posta bulunamadı.");

            contact.Email = match.Email;
            contact.Phone ??= match.Phone;
            contact.SourceUrl ??= match.ProfileUrl;
            contact.EmploymentStartDate ??= match.EmploymentStartDate;
            contact.Location ??= match.Location;
            contact.Headline ??= match.Headline;

            // Gercek isim (soyad artik gizli degil) match'ten gelirse tercih edilir.
            if (!string.IsNullOrWhiteSpace(match.Name))
                contact.Name = match.Name;

            StringLengthGuard.Apply(contact);

            // "E-posta bulundu" kriteri artik karsilanabilir; puan yeniden hesaplanir.
            var company = contact.Company;
            var analysis = CompanyController.ParseAnalysis(company.AiAnalysis);
            company.Score = _scoring.ScoreCompany(analysis, site: null, company.Contacts).Total;

            await _db.SaveChangesAsync(ct);
            return new RevealOutcome(true, $"{contact.Name} için e-posta açıldı: {contact.Email}");
        }
        catch (MissingApiKeyException ex)
        {
            return new RevealOutcome(false, ex.Message, IsError: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E-posta açma başarısız: {ContactId}", contactId);
            return new RevealOutcome(false, $"E-posta açılamadı: {ex.Message}", IsError: true);
        }
    }
}
