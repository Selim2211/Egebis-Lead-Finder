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
                p.Location, SourceLabel(p.Source), p.SourceUrl
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
        Add("Haber puanı (son 30 gün)", c.NewsBoost);
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
                SourceLabel(p.Source), p.SourceUrl, p.TenureText(), c.Leads.Any(l => l.ContactId == p.Id) ? "Evet" : "Hayır"
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
                p.Name, p.Industry, p.NaceCode, p.NaceProducts, string.Join(", ", SearchProfileTransfer.RegionsOf(p).Select(r => r.Name)), dto.RegionKeys.Length > 1 ? null : p.City, p.MaxCompanies,
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
            var sheet = workbook.Worksheets.Add(SheetName(Loc.T(table.Name), workbook));

            // Basliklar da temizlenir: karsilastirmada sutun basligi firma adidir.
            for (var c = 0; c < table.Headers.Length; c++)
                sheet.Cell(1, c + 1).Value = Clean(Loc.T(table.Headers[c]));

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                for (var c = 0; c < row.Length; c++)
                {
                    WriteCell(sheet.Cell(r + 2, c + 1), row[c]);
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

    /// <summary>Toplu firma aktariminda tek dosyaya en fazla bu kadar firma (her biri ayri sayfa).</summary>
    public const int MaxBulkCompanies = 100;

    /// <summary>
    /// Secilen firmalar tek Excel dosyasinda: ilk sayfa "Firmalar" (liste, her ad kendi sayfasina baglanti),
    /// sonra her firma kendi sayfasinda. Firma sayfasi, tek firma "Excel'e aktar"daki bilginin aynisidir:
    /// Firma (Alan/Deger), Kisiler ve Leadler bolumleri alt alta. Sayfa adi firma adidir (Excel kurali: en fazla 31 karakter, benzersiz).
    /// </summary>
    public static byte[] ToXlsxCompanies(IReadOnlyList<Company> companies)
    {
        using var workbook = new XLWorkbook();
        var index = workbook.Worksheets.Add(SheetName(Loc.T("Firmalar"), workbook));
        string[] indexHeaders = { "Firma", "Web sitesi", "Şehir", "Ülke", "NACE", "Puan", "Uygunluk (%)", "Kişi sayısı", "Lead sayısı" };
        for (var c = 0; c < indexHeaders.Length; c++) index.Cell(1, c + 1).Value = Loc.T(indexHeaders[c]);
        StyleHeader(index.Range(1, 1, 1, indexHeaders.Length));
        index.SheetView.FreezeRows(1);

        for (var i = 0; i < companies.Count; i++)
        {
            var company = companies[i];
            var sheet = workbook.Worksheets.Add(SheetName(string.IsNullOrWhiteSpace(company.Name) ? $"Firma {i + 1}" : company.Name, workbook));
            WriteCompanySheet(sheet, company);

            var row = i + 2;
            var link = index.Cell(row, 1);
            link.Value = Clean(company.Name);
            link.SetHyperlink(new XLHyperlink($"'{sheet.Name.Replace("'", "''")}'!A1"));
            object?[] values = { company.Website, company.City, company.Country, company.NaceCode, company.Score, company.FitScore,
                company.Contacts.Count, company.Leads.Count };
            for (var c = 0; c < values.Length; c++) WriteCell(index.Cell(row, c + 2), values[c]);
        }

        if (companies.Count > 0) index.Range(1, 1, companies.Count + 1, indexHeaders.Length).SetAutoFilter();
        index.Columns().AdjustToContents(1, Math.Min(companies.Count + 1, 200), 8, 60);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Firma sayfasi: baslik, sonra Firma / Kisiler / Leadler bolumleri (her biri bolum adi + baslik satiri + satirlar).</summary>
    private static void WriteCompanySheet(IXLWorksheet sheet, Company company)
    {
        var title = sheet.Cell(1, 1);
        title.Value = Clean(company.Name);
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;

        var row = 3;
        var widest = 2;
        foreach (var table in CompanyDetailTables(company))
        {
            var section = sheet.Cell(row, 1);
            section.Value = Loc.T(table.Name);
            section.Style.Font.Bold = true;
            section.Style.Font.FontSize = 12;
            section.Style.Font.FontColor = XLColor.FromHtml("#0369A1");
            row++;

            for (var c = 0; c < table.Headers.Length; c++) sheet.Cell(row, c + 1).Value = Clean(Loc.T(table.Headers[c]));
            StyleHeader(sheet.Range(row, 1, row, table.Headers.Length));
            row++;

            var first = row;
            foreach (var values in table.Rows)
            {
                for (var c = 0; c < values.Length; c++) WriteCell(sheet.Cell(row, c + 1), values[c]);
                row++;
            }
            if (table.Rows.Count == 0)
            {
                sheet.Cell(row, 1).Value = Loc.T("Kayıt yok");
                sheet.Cell(row, 1).Style.Font.Italic = true;
                sheet.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#64748B");
                row++;
            }
            else
            {
                var body = sheet.Range(first, 1, row - 1, table.Headers.Length);
                body.Style.Alignment.WrapText = true;
                body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            }
            widest = Math.Max(widest, table.Headers.Length);
            row++; // bolumler arasi bos satir
        }

        // Sutun genislikleri: A alan/ad, B deger/unvan genis; digerleri icerige gore.
        sheet.Column(1).Width = 34;
        sheet.Column(2).Width = 60;
        for (var c = 3; c <= widest; c++) sheet.Column(c).AdjustToContents(1, Math.Min(row, 300), 10, 40);
    }

    private static void StyleHeader(IXLRange header)
    {
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
    }

    /// <summary>Tek hucreyi turune gore yazar (sayi sayi, tarih tarih, metin her zaman metin).</summary>
    private static void WriteCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            // Ondalikli sayilar sayi olarak yazilir (metin degil): Excel'de toplanir, siralanir.
            case double dbl when double.IsFinite(dbl): cell.Value = Math.Round(dbl, 2, MidpointRounding.AwayFromZero); break;
            case float f when float.IsFinite(f): cell.Value = Math.Round((double)f, 2, MidpointRounding.AwayFromZero); break;
            case double or float: break; // NaN / sonsuz: bos hucre
            case decimal m: cell.Value = Math.Round(m, 2, MidpointRounding.AwayFromZero); break;
            case bool b: cell.Value = Loc.T(b ? "Evet" : "Hayır"); break;
            case DateTime d: cell.Value = d; cell.Style.DateFormat.Format = "dd.MM.yyyy HH:mm"; break;
            case DateOnly day: cell.Value = day.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "dd.MM.yyyy"; break;
            // Metin her zaman metin olarak yazilir: "=..." ile baslayan deger formul olmaz.
            default: cell.Value = Clean(Loc.T(value!.ToString() ?? string.Empty)); break;
        }
    }

    /// <summary>Excel sayfa adi kurali: en fazla 31 karakter, []:*?/\ yok, ayni ad iki kez olmaz.</summary>
    private static string SheetName(string name, XLWorkbook workbook)
    {
        var clean = new string(Clean(name).Where(ch => "[]:*?/\\".IndexOf(ch) < 0 && ch >= ' ').ToArray()).Trim('\'', ' ');
        if (clean.Length == 0) clean = "Sayfa";
        if (clean.Length > 31) clean = clean[..31];
        var candidate = clean;
        for (var n = 2; workbook.Worksheets.Contains(candidate); n++)
            candidate = (clean.Length > 27 ? clean[..27] : clean) + $" ({n})";
        return candidate;
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

    // Kultur verisine (ICU) bagli kalmadan ondalik ayirici virgul.
    private static readonly NumberFormatInfo Tr = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "" };

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime d => d.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
        DateOnly day => day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        bool b => Loc.T(b ? "Evet" : "Hayır"),
        // ";" ayiricili CSV Turkce Excel icindir: ondalik ayirici virgul ("45,5"; "45.5" tarih sanilabilir).
        double nd when !double.IsFinite(nd) => string.Empty,
        float nf when !float.IsFinite(nf) => string.Empty,
        double or float or decimal => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("0.##", Tr),
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

    /// <summary>Kisinin nereden bulundugu, ekrandaki adla (Firma sitesi / LinkedIn + Apollo / Apollo).</summary>
    public static string SourceLabel(ContactSource source) => source switch
    {
        ContactSource.ApolloSearch => "Apollo",
        ContactSource.LinkedInApollo => "LinkedIn + Apollo",
        _ => "Firma sitesi"
    };

    /// <summary>Excel hucresine sigan en uzun metin (Excel siniri 32.767 karakter).</summary>
    public const int MaxCellChars = 32_000;

    /// <summary>
    /// Web sitesinden gelen metni Excel/CSV icin temizler: XML'de gecersiz kontrol karakterleri (sekme ve satir sonu haric)
    /// ve yarim kalmis emoji parcalari atilir (aksi halde Excel dosyasi olusmaz ya da "onarilsin mi" der);
    /// Excel hucre siniri asilmasin diye cok uzun metin kisaltilir.
    /// </summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(Math.Min(value.Length, MaxCellChars + 1));
        for (var i = 0; i < value.Length && sb.Length < MaxCellChars; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { sb.Append(ch).Append(value[++i]); }
                continue;
            }
            if (char.IsLowSurrogate(ch)) continue;
            if (ch < ' ' && ch is not ('\t' or '\n' or '\r')) { sb.Append(' '); continue; }
            if (ch is '￾' or '￿') continue;
            sb.Append(ch);
        }
        if (sb.Length >= MaxCellChars && value.Length > sb.Length) sb.Append('…');
        return sb.ToString();
    }

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
