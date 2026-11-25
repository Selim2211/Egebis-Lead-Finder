using EgebisLeadFinder.Data;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

/// <summary>Tek firmanin tum bilgisini (firma + kisiler + leadler) Excel olarak indirir. Ice aktarma yoktur.</summary>
[Authorize]
public class DataTransferController : Controller
{
    private readonly ApplicationDbContext _db;

    public DataTransferController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Export(int companyId, CancellationToken ct)
    {
        var company = await _db.Companies.AsNoTracking()
            .Include(c => c.Contacts)
            .Include(c => c.Leads).ThenInclude(l => l.SentEmails)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return NotFound();

        var tables = ExportService.CompanyDetailTables(company);
        AuditActionFilter.SetAuditSummary(HttpContext,
            $"Firma Excel'e aktarıldı: {company.Name} ({company.Contacts.Count} kişi, {company.Leads.Count} lead)");

        var slug = new string(company.Name.Where(char.IsLetterOrDigit).Take(40).ToArray());
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
        return File(ExportService.ToXlsx(tables),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"firma-{(slug.Length > 0 ? slug : companyId.ToString())}-{stamp}.xlsx");
    }

    /// <summary>
    /// Toplu aktarim: secilen firmalar tek Excel dosyasinda, her firma kendi sayfasinda (tek firma aktarimiyla ayni bilgi).
    /// Firmalar secim sirasiyla yazilir; olmayan (silinmis) firmalar atlanir.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportMany([FromQuery] int[]? ids, CancellationToken ct)
    {
        var wanted = (ids ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToList();
        if (wanted.Count == 0)
            return BadRequest("Excel'e aktarmak için en az bir firma seçin.");
        if (wanted.Count > ExportService.MaxBulkCompanies)
            return BadRequest($"Tek seferde en fazla {ExportService.MaxBulkCompanies} firma aktarılabilir.");

        var found = await _db.Companies.AsNoTracking()
            .Where(c => wanted.Contains(c.Id))
            .Include(c => c.Contacts)
            .Include(c => c.Leads).ThenInclude(l => l.SentEmails)
            .AsSplitQuery()
            .ToListAsync(ct);
        if (found.Count == 0) return NotFound();

        var companies = found.OrderBy(c => wanted.IndexOf(c.Id)).ToList();
        AuditActionFilter.SetAuditSummary(HttpContext,
            $"Firmalar toplu Excel'e aktarıldı: {companies.Count} firma ({companies.Sum(c => c.Contacts.Count)} kişi, {companies.Sum(c => c.Leads.Count)} lead)");

        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
        return File(ExportService.ToXlsxCompanies(companies),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"firmalar-{companies.Count}-firma-{stamp}.xlsx");
    }
}
