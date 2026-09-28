using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EgebisLeadFinder.Controllers;

/// <summary>Denetim kayitlari (yalnizca yonetici): giris/cikis ve tum islemler, filtre ve CSV.</summary>
[Authorize(Roles = nameof(UserRole.Admin))]
public class AuditController : Controller
{
    private const int PageSize = 50;
    private readonly ApplicationDbContext _db;

    public AuditController(ApplicationDbContext db) => _db = db;

    /// <summary>Giris/cikis olaylari; filtre listesinde en ustte.</summary>
    public static readonly (string Code, string Label)[] AuthActions =
    {
        ("login", "Giriş"),
        ("login.failed", "Hatalı giriş"),
        ("login.locked", "Kilitli hesap girişi"),
        ("logout", "Çıkış"),
        ("user.setup", "İlk yönetici"),
        ("user.changepassword", "Şifre değiştirme"),
        ("search.finished", "Arama tamamlandı"),
        ("search.cancelled", "Arama iptal edildi"),
        ("sequence.sent", "Dizi maili gönderildi (otomatik)"),
        ("salesforce.connect", "Salesforce bağlandı")
    };

    [HttpGet]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, int? userId, string? op, string? q,
        bool onlyFailed = false, int page = 1, CancellationToken ct = default)
    {
        var query = Filter(from, to, userId, op, q, onlyFailed);
        var total = await query.CountAsync(ct);
        page = PagerModel.Clamp(page, total, PageSize);

        var model = new AuditListViewModel
        {
            Entries = await query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
                .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct),
            Pager = new PagerModel { Page = page, PageSize = PageSize, TotalItems = total },
            From = from,
            To = to,
            UserId = userId,
            Action = op,
            Q = q,
            OnlyFailed = onlyFailed,
            Users = await _db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(ct),
            Actions = ActionOptions()
        };
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Export(DateTime? from, DateTime? to, int? userId, string? op, string? q,
        bool onlyFailed = false, CancellationToken ct = default)
    {
        var rows = await Filter(from, to, userId, op, q, onlyFailed)
            .OrderByDescending(a => a.At).Take(ExportService.MaxRows).ToListAsync(ct);

        var table = new ExportTable("Audit",
            new[] { "Tarih", "Kullanıcı", "İşlem", "Özet", "Kayıt", "Sonuç", "IP", "Tarayıcı" },
            rows.Select(a => new object?[]
            {
                a.At.ToLocalTime(), a.UserName, ActionLabel(a.Action), a.Summary,
                a.EntityType is null ? null : $"{a.EntityType} #{a.EntityId}",
                a.Success ? "Başarılı" : "Başarısız", a.Ip, a.UserAgent
            }).ToList());

        return File(ExportService.ToCsv(table), "text/csv; charset=utf-8", $"audit-{DateTime.Now:yyyy-MM-dd-HHmm}.csv");
    }

    private IQueryable<AuditLog> Filter(DateTime? from, DateTime? to, int? userId, string? op, string? q, bool onlyFailed)
    {
        var query = _db.AuditLogs.AsNoTracking();

        // Tarihler kullanicinin yerel gunu olarak girilir; "bitis" gunun sonuna kadar kapsar.
        if (from is not null)
        {
            var start = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Local).ToUniversalTime();
            query = query.Where(a => a.At >= start);
        }
        if (to is not null)
        {
            var end = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
            query = query.Where(a => a.At < end);
        }
        if (userId is not null) query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(op)) query = query.Where(a => a.Action == op);
        if (onlyFailed) query = query.Where(a => !a.Success);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(a => EF.Functions.ILike(a.Summary ?? "", term)
                || EF.Functions.ILike(a.UserName, term) || EF.Functions.ILike(a.Ip ?? "", term));
        }
        return query;
    }

    public static List<(string Code, string Label)> ActionOptions() =>
        AuthActions
            .Concat(AuditActionFilter.Known.Values.Select(v => (v.Code, v.Label)))
            .GroupBy(x => x.Code)
            .Select(g => g.First())
            .ToList();

    public static string ActionLabel(string code)
    {
        foreach (var (c, label) in ActionOptions())
            if (c == code) return label;
        return code;
    }
}
