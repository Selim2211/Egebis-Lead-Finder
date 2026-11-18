using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using EgebisLeadFinder.Localization;
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
                "Firma", "Web sitesi", "Adres", "Şehir", "Ülke", "Sektör", "NACE", "Puan", "Uygunluk (%)", "Neden", "Sinyal", "Aşama",
                "Kişi sayısı", "Lead sayısı", "AI özeti", "Satış önerisi", "Eklenme"
            },
            list.Select(c =>
            {
                var rating = Parse<CompanyRating>(c.RatingJson);
                var analysis = Parse<CompanyAnalysis>(c.AiAnalysis);
                return new object?[]
                {
                    c.Name, c.Website, c.Address, c.City, c.Country, analysis?.Industry ?? c.Industry, c.NaceCode, c.Score,
                    c.FitScore, c.FitReason,
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

    /// <summary>
    /// Tek firmanin tum bilgisi: "Firma" (Alan/Deger listesi), "Kisiler" ve "Leadler" sayfalari.
    /// Her satirin hucre sayisi basligin sutun sayisina esittir (kayma olmaz); kisi ve lead yoksa baslik satiri yine yazilir.
    /// </summary>
    public static List<ExportTable> CompanyDetailTables(Company c)
    {
        var a = Parse<CompanyAnalysis>(c.AiAnalysis);
        var r = Parse<CompanyRating>(c.RatingJson);
        string? Join(IEnumerable<string?>? items) =>
            items is null ? null : string.Join("; ", items.Where(x => !string.IsNullOrWhiteSpace(x)));
        DateTime? Local(DateTime? d) => d?.ToLocalTime();

        var rows = new List<object?[]>();
        void Add(string field, object? value) => rows.Add(new object?[] { field, value });

        Add("Firma adı", c.Name);
        Add("Web sitesi", c.Website);
        Add("Alan adı", c.Domain);
        Add("Sektör", a?.Industry ?? c.Industry);
        Add("NACE kodu", c.NaceCode);
        Add("NACE faaliyet adı", NaceCatalog.NameOf(c.NaceCode));
        Add("Şehir", c.City);
        Add("Ülke", c.Country);
        Add("Adres", c.Address);
        Add("Lead puanı", c.Score);
        Add("Uygunluk (%)", c.FitScore);
        Add("Uygunluk segmenti", c.FitSegment);
        Add("Uygunluk gerekçesi", c.FitReason);
        Add("Aşama", SalesforceRecordMapper.StageLabel(c));
        Add("İdeal müşteri profiline uyuyor", c.IcpMatch ? "Evet" : "Hayır");
        Add("Değerlendirme durumu", c.EvaluationStatus switch
        {
            EvaluationStatus.Evaluated => "Değerlendirildi",
            EvaluationStatus.Disqualified => "Elendi",
            _ => "İncelenemedi"
        });
        Add("Değerlendirme notu", c.EvaluationNote);
        Add("İşleme hatası", c.ProcessingError);
        Add("Açıklama", c.Description);

        Add("Yapay zekâ: üretici firma", a is null ? null : a.Manufacturer ? "Evet" : "Hayır");
        Add("Yapay zekâ: ürünler", Join(a?.Products));
        Add("Yapay zekâ: SAP durumu", a?.Sap switch
        {
            null => null,
            "yes" => "SAP kullanıyor",
            "likely" => "SAP kullanma ihtimali yüksek",
            "no" => "SAP kullanmıyor",
            _ => "Bilinmiyor"
        });
        Add("Yapay zekâ: SAP kanıtı", a?.SapEvidence);
        Add("Yapay zekâ: SAP hizmeti satıyor (rakip)", a is null ? null : a.SapVendor ? "Evet" : "Hayır");
        Add("Yapay zekâ: çalışan tahmini", a?.EmployeeSizeHint);
        Add("Yapay zekâ: potansiyel", a is null ? null : a.Potential ? "Evet" : "Hayır");
        Add("Yapay zekâ: değerlendirme", a?.Reason);
        Add("Yapay zekâ: eşleşen kriterler", Join(a?.MatchedSignals));
        Add("Yapay zekâ: modeli", a?.AiModel);

        Add("Ön araştırma: sinyal", RatingSignalDisplay.HasSignal(c.RatingSignal) ? RatingSignalDisplay.Label(c.RatingSignal) : null);
        Add("Ön araştırma: özet", r?.Summary);
        Add("Ön araştırma: kuruluş", r?.FoundingInfo);
        Add("Ön araştırma: ölçek", r?.ScaleInfo);
        Add("Ön araştırma: finansal bilgi", r?.FinancialInfo);
        Add("Ön araştırma: ortaklar / yöneticiler", r?.Owners);
        Add("Ön araştırma: müşteriler", Join(r?.Customers));
        Add("Ön araştırma: projeler", Join(r?.Projects));
        Add("Ön araştırma: büyüme işaretleri", Join(r?.GrowthSignals));
        Add("Ön araştırma: risk işaretleri", Join(r?.RiskSignals.Select(x => $"{x.Text} ({x.Severity})")));
        Add("Ön araştırma: satış önerisi", r?.SalesApproach);
        Add("Ön araştırma: tarih", Local(c.RatedAt));

        Add("İletişim kuruldu", Local(c.ContactedAt));
        Add("E-posta gönderildi", Local(c.EmailSentAt));
        Add("Proje başladı", Local(c.ProjectStartedAt));
        Add("Eklenme tarihi", Local(c.CreatedAt));

        var firm = new ExportTable("Firma", new[] { "Alan", "Değer" }, rows);

        var people = new ExportTable("Kişiler",
            new[] { "Ad soyad", "Ünvan", "E-posta", "E-posta durumu", "Telefon", "Konum", "Başlık", "Kaynak", "Profil bağlantısı", "Çalışma süresi", "Lead var mı" },
            c.Contacts.OrderByDescending(p => p.TitleScore).ThenBy(p => p.Id).Select(p => new object?[]
            {
                p.Name, p.Title, p.Email, EmailStatusDisplay.Label(p.EmailStatus, p.Email), p.Phone, p.Location, p.Headline,
                p.Source.ToString(), p.SourceUrl, p.TenureText(), c.Leads.Any(l => l.ContactId == p.Id) ? "Evet" : "Hayır"
            }).ToList());

        var leads = new ExportTable("Leadler",
            new[] { "Kişi", "Ünvan", "E-posta", "Durum", "Puan", "Gönderilen mail", "Son mail", "İletişim", "Cevap", "Notlar", "Oluşturma" },
            c.Leads.OrderByDescending(l => l.CreatedAt).Select(l =>
            {
                var person = c.Contacts.FirstOrDefault(p => p.Id == l.ContactId);
                var lastMail = l.SentEmails.Count > 0 ? l.SentEmails.Max(e => e.SentAt) : l.SentAt;
                return new object?[]
                {
                    person?.Name, person?.Title, person?.Email, LeadStatusDisplay.Label(l.Status), l.Score, l.SentEmails.Count,
                    Local(lastMail), Local(l.ContactedAt), Local(l.RepliedAt), l.Notes, l.CreatedAt.ToLocalTime()
                };
            }).ToList());

        return new List<ExportTable> { firm, people, leads };
    }

    public static ExportTable SearchProfileTable(IEnumerable<SearchProfile> profiles, int? me) => new("Arama Profilleri",
        new[] { "Profil", "Sektör", "NACE", "Ürünler", "Ülke / bölge", "İl / bölge", "En fazla firma", "Paylaşım", "Oluşturma" },
        profiles.Select(p =>
        {
            var dto = SearchProfileDto.From(p, me, false);
            return new object?[]
            {
                p.Name, p.Industry, p.NaceCode, p.NaceProducts, dto.RegionName, p.City, p.MaxCompanies,
                dto.OwnerName is not null ? $"Paylaşan: {dto.OwnerName}" : p.IsPublic ? "Herkese açık" : "Size özel",
                p.CreatedAt.ToLocalTime()
            };
        }).ToList());

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
            var sheet = workbook.Worksheets.Add(Loc.T(table.Name));

            for (var c = 0; c < table.Headers.Length; c++)
                sheet.Cell(1, c + 1).Value = Loc.T(table.Headers[c]);

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
                        default: cell.Value = Clean(Loc.T(row[c]!.ToString() ?? string.Empty)); break;
                    }
                }
            }

            var header = sheet.Range(1, 1, 1, table.Headers.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
            sheet.SheetView.FreezeRows(1);
            if (table.Rows.Count > 0) sheet.Range(1, 1, table.Rows.Count + 1, table.Headers.Length).SetAutoFilter();
            sheet.Columns().AdjustToContents(1, Math.Min(table.Rows.Count + 1, 200), 8, 60);
            // Uzun metinler (ozet, gerekce, notlar) hucre icinde satira sarilir; sutunlar kaymaz.
            if (table.Rows.Count > 0)
            {
                var body = sheet.Range(2, 1, table.Rows.Count + 1, table.Headers.Length);
                body.Style.Alignment.WrapText = true;
                body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Turkce Excel'in dogru acmasi icin UTF-8 BOM ve ";" ayirici.</summary>
    public static byte[] ToCsv(ExportTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', table.Headers.Select(h => Escape(Loc.T(h)))));
        foreach (var row in table.Rows)
            sb.AppendLine(string.Join(';', row.Select(v => Escape(Loc.T(Format(v))))));

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
