using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>Disa aktarilacak tek bir tablo (Excel'de bir sayfa, CSV'de tek dosya).</summary>
public record ExportTable(string Name, string[] Headers, List<object?[]> Rows);

/// <summary>Firma/kisi/lead listelerini Excel (.xlsx) veya CSV'ye cevirir.</summary>
public static class ExportService
{
    public const int MaxRows = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static List<ExportTable> CompanyTables(IEnumerable<Company> companies)
    {
        var list = companies.ToList();

        var firms = new ExportTable("Firmalar",
            new[]
            {
                "Firma", "Web sitesi", "Şehir", "Ülke", "Sektör", "NACE", "Puan", "Sinyal", "Aşama",
                "Kişi sayısı", "Lead sayısı", "AI özeti", "Satış önerisi", "Eklenme"
            },
            list.Select(c =>
            {
                var rating = Parse<CompanyRating>(c.RatingJson);
                var analysis = Parse<CompanyAnalysis>(c.AiAnalysis);
                return new object?[]
                {
                    c.Name, c.Website, c.City, c.Country, analysis?.Industry ?? c.Industry, c.NaceCode, c.Score,
                    RatingSignalDisplay.HasSignal(c.RatingSignal) ? RatingSignalDisplay.Label(c.RatingSignal) : null,
                    SalesforceRecordMapper.StageLabel(c), c.Contacts.Count, c.Leads.Count,
                    rating?.Summary ?? analysis?.Reason, rating?.SalesApproach, c.CreatedAt.ToLocalTime()
                };
            }).ToList());

        var people = new ExportTable("Kişiler",
            new[] { "Firma", "Ad soyad", "Ünvan", "E-posta", "E-posta durumu", "Telefon", "Konum", "Kaynak", "Profil" },
            list.SelectMany(c => c.Contacts.Select(p => new object?[]
            {
                c.Name, p.Name, p.Title, p.Email, EmailStatusDisplay.Label(p.EmailStatus, p.Email), p.Phone,
                p.Location, p.Source.ToString(), p.SourceUrl
            })).ToList());

        return new List<ExportTable> { firms, people };
    }

    public static ExportTable LeadTable(IEnumerable<Lead> leads, DateTime now)
    {
        return new ExportTable("Leadler",
            new[]
            {
                "Firma", "Kişi", "Ünvan", "E-posta", "E-posta durumu", "Telefon", "Durum", "Puan",
                "Son mail", "Gönderilen mail", "Bekleme (gün)", "Cevap", "Şehir", "Notlar", "Oluşturma"
            },
            leads.Select(l =>
            {
                var lastMail = l.SentEmails.Count > 0 ? l.SentEmails.Max(e => e.SentAt) : l.SentAt;
                var lastTouch = lastMail ?? l.ContactedAt;
                return new object?[]
                {
                    l.Company?.Name, l.Contact?.Name, l.Contact?.Title, l.Contact?.Email,
                    EmailStatusDisplay.Label(l.Contact?.EmailStatus ?? EmailStatus.Unchecked, l.Contact?.Email),
                    l.Contact?.Phone, LeadStatusDisplay.Label(l.Status), l.Score,
                    lastMail?.ToLocalTime(), l.SentEmails.Count,
                    lastTouch is null || l.RepliedAt is not null ? null : (int)(now - lastTouch.Value).TotalDays,
                    l.RepliedAt?.ToLocalTime(), l.Company?.City, l.Notes, l.CreatedAt.ToLocalTime()
                };
            }).ToList());
    }

    public static byte[] ToXlsx(IEnumerable<ExportTable> tables)
    {
        using var workbook = new XLWorkbook();

        foreach (var table in tables)
        {
            var sheet = workbook.Worksheets.Add(table.Name);

            for (var c = 0; c < table.Headers.Length; c++)
                sheet.Cell(1, c + 1).Value = table.Headers[c];

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                for (var c = 0; c < row.Length; c++)
                {
                    var cell = sheet.Cell(r + 2, c + 1);
                    switch (row[c])
                    {
                        case null: break;
                        case int i: cell.Value = i; break;
                        case DateTime d: cell.Value = d; cell.Style.DateFormat.Format = "dd.MM.yyyy HH:mm"; break;
                        // Metin her zaman metin olarak yazilir: "=..." ile baslayan deger formul olmaz.
                        default: cell.Value = Clean(row[c]!.ToString()); break;
                    }
                }
            }

            var header = sheet.Range(1, 1, 1, table.Headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
            sheet.SheetView.FreezeRows(1);
            if (table.Rows.Count > 0) sheet.Range(1, 1, table.Rows.Count + 1, table.Headers.Length).SetAutoFilter();
            sheet.Columns().AdjustToContents(1, Math.Min(table.Rows.Count + 1, 200), 8, 60);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Turkce Excel'in dogru acmasi icin UTF-8 BOM ve ";" ayirici.</summary>
    public static byte[] ToCsv(ExportTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', table.Headers.Select(Escape)));
        foreach (var row in table.Rows)
            sb.AppendLine(string.Join(';', row.Select(v => Escape(Format(v)))));

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime d => d.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Clean(value.ToString())
    };

    private static string Escape(string value)
    {
        // CSV formul enjeksiyonu: Excel "=", "+", "-", "@" ile baslayan hucreyi formul sayar.
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static string Clean(string? value) => (value ?? string.Empty).Replace("\0", string.Empty);

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
