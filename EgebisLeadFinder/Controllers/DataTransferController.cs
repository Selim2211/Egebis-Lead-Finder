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
}
