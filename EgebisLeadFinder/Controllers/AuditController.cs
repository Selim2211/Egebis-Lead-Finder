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

    /// <summary>
    /// Filtredeki kayitlari indirir: Excel (varsayilan; kayitlar + ozet + filtre sayfasi) veya CSV.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Export(DateTime? from, DateTime? to, int? userId, string? op, string? q,
        bool onlyFailed = false, string? format = null, CancellationToken ct = default)
    {
        var query = Filter(from, to, userId, op, q, onlyFailed);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.At).Take(ExportService.MaxRows).ToListAsync(ct);
        var table = LogTable(rows);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return File(ExportService.ToCsv(table), "text/csv; charset=utf-8", $"audit-{stamp}.csv");

        string? userName = userId is null ? null
            : await _db.Users.Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);
        var filters = FilterTable(from, to, userName, op, q, onlyFailed, total, rows.Count, User.Identity?.Name);

        return File(ExportService.ToXlsx(new[] { table, SummaryTable(rows), filters }),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"audit-{stamp}.xlsx");
    }

    public static ExportTable LogTable(IEnumerable<AuditLog> rows) => new("Audit kayıtları",
        new[] { "Tarih", "Kullanıcı", "İşlem", "İşlem kodu", "Özet", "Kayıt", "Sonuç", "IP", "Tarayıcı" },
        rows.Select(a => new object?[]
        {
            a.At.ToLocalTime(), a.UserName, ActionLabel(a.Action), a.Action, a.Summary,
            a.EntityType is null ? null : $"{a.EntityType} #{a.EntityId}",
            a.Success ? "Başarılı" : "Başarısız", a.Ip, a.UserAgent
        }).ToList());

    /// <summary>Kullanici ve islem bazinda sayim: kim ne kadar, kac basarisiz.</summary>
    public static ExportTable SummaryTable(IReadOnlyCollection<AuditLog> rows) => new("Özet",
        new[] { "Kullanıcı", "İşlem", "Toplam", "Başarısız", "İlk", "Son" },
        rows.GroupBy(a => (a.UserName, a.Action))
            .OrderBy(g => g.Key.UserName, StringComparer.OrdinalIgnoreCase).ThenByDescending(g => g.Count())
            .Select(g => new object?[]
            {
                g.Key.UserName, ActionLabel(g.Key.Action), g.Count(), g.Count(a => !a.Success),
                g.Min(a => a.At).ToLocalTime(), g.Max(a => a.At).ToLocalTime()
            }).ToList());

    public static ExportTable FilterTable(DateTime? from, DateTime? to, string? userName, string? op, string? q,
        bool onlyFailed, int total, int exported, string? exportedBy)
    {
        var rows = new List<object?[]>
        {
            new object?[] { "Oluşturma", DateTime.Now },
            new object?[] { "Oluşturan", exportedBy },
            new object?[] { "Başlangıç", from?.ToString("dd.MM.yyyy") ?? "—" },
            new object?[] { "Bitiş", to?.ToString("dd.MM.yyyy") ?? "—" },
            new object?[] { "Kullanıcı", userName ?? "Tümü" },
            new object?[] { "İşlem", string.IsNullOrWhiteSpace(op) ? "Tümü" : ActionLabel(op) },
            new object?[] { "Arama", string.IsNullOrWhiteSpace(q) ? "—" : q },
            new object?[] { "Yalnızca başarısızlar", onlyFailed ? "Evet" : "Hayır" },
            new object?[] { "Filtredeki kayıt", total },
            new object?[] { "Dosyadaki kayıt", exported }
        };
        if (total > exported)
            rows.Add(new object?[] { "Not", $"Dosyaya en yeni {exported} kayıt alındı; daha eskiler için tarih aralığını daraltın." });
        return new ExportTable("Filtre", new[] { "Alan", "Değer" }, rows);
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
