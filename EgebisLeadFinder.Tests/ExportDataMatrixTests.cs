using ClosedXML.Excel;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Her türden firma/kişi/lead verisiyle (tam dolu, yalnız ad, yabancı, kirli karakterli, elenmiş, incelenemeyen, çok kişili;
/// firma sitesi / LinkedIn / Apollo kaynaklı kişiler) tüm Excel ve CSV dışa aktarımları: her hücre kaynak değerle aynı sütunda.
/// </summary>
public class ExportDataMatrixTests
{
    public const string Tag = "ZZ Test";
    public const string DomainTail = ".export-test.invalid";

    public static DateTime U(int y, int m, int d, int h = 9) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    public static List<Company> Build()
    {
        var full = new Company
        {
            Name = $"{Tag} 1 Tam Dolu Makina A.Ş.", Website = "https://tam" + DomainTail, Domain = "tam" + DomainTail,
            Industry = "Makine imalatı", NaceCode = "28.22.01", City = "Manisa", Country = "Türkiye",
            Address = "Organize Sanayi Bölgesi 3. Cadde No:12, 45030 Yunusemre/Manisa", FitScore = 92, FitSegment = "Üretici",
            FitReason = "Kaldırma ekipmanı üreticisi; SAP kullanma ihtimali yüksek.", Description = "Vinç ve kaldırma ekipmanları",
            AiAnalysis = "{\"industry\":\"Kaldırma ekipmanları\",\"manufacturer\":true,\"products\":[\"Vinç\",\"Caraskal\"],\"sap\":\"likely\",\"sapEvidence\":\"İK ilanında SAP MM\",\"potential\":true,\"reason\":\"Büyük üretici\",\"employeeSizeHint\":\"250-500\",\"matchedSignals\":[\"Üretim\",\"İhracat\"],\"aiModel\":\"gemini\"}",
            RatingJson = "{\"signal\":\"guclu\",\"summary\":\"Köklü aile şirketi\",\"foundingInfo\":\"1978\",\"scaleInfo\":\"3 fabrika\",\"customers\":[\"Ford Otosan\",\"Arçelik\"],\"projects\":[\"Yeni tesis\"],\"growthSignals\":[\"İhracat artışı\"],\"riskSignals\":[{\"text\":\"Dava\",\"severity\":\"orta\"}],\"salesApproach\":\"Bilgi işlem müdürüyle görüş\"}",
            RatingSignal = "Guclu", RatedAt = U(2026, 11, 20), Score = 88, NewsBoost = 4, IcpMatch = true,
            ContactedAt = U(2026, 11, 21), EmailSentAt = U(2026, 11, 22), CreatedAt = U(2026, 11, 1)
        };
        var p1 = new Contact { Name = "Ayşe Yılmaz", Title = "Bilgi İşlem Müdürü", Email = "ayse.yilmaz@tam" + DomainTail, EmailStatus = EmailStatus.Valid,
            Phone = "0532 111 22 33", Location = "Manisa, Türkiye", Headline = "IT Manager | SAP", Source = ContactSource.ApolloSearch,
            ApolloId = "64a1b2c3d4e5f6a7b8c9d0e1", SourceUrl = "https://www.linkedin.com/in/ayse-yilmaz-test", TitleScore = 95,
            EmploymentStartDate = new DateOnly(2019, 3, 1) };
        var p2 = new Contact { Name = "Mehmet Öztürk", Title = "Genel Müdür", Email = null, Phone = "+90 (236) 555 00 11",
            Source = ContactSource.LinkedInApollo, SourceUrl = "https://tr.linkedin.com/in/mehmet-ozturk-test", TitleScore = 60 };
        var p3 = new Contact { Name = "İletişim", Title = null, Email = "info@tam" + DomainTail, EmailStatus = EmailStatus.Role,
            Phone = "02365550000", Source = ContactSource.Website, SourceUrl = "https://tam" + DomainTail + "/iletisim", TitleScore = 0 };
        full.Contacts.AddRange(new[] { p1, p2, p3 });
        full.Leads.Add(new Lead { Contact = p1, Score = 88, Status = LeadStatus.Gonderildi, Notes = "Salı ara.\nTeklif: 2. hafta; \"acil\"", SentAt = U(2026, 11, 22), CreatedAt = U(2026, 11, 21) });
        full.Leads.Add(new Lead { Contact = p2, Score = 60, Status = LeadStatus.Yeni, CreatedAt = U(2026, 11, 23) });

        var minimal = new Company { Name = $"{Tag} 2 Sadece Ad", Score = 0, CreatedAt = U(2026, 11, 2) };

        var foreign = new Company
        {
            Name = $"{Tag} 3 Müller & Söhne GmbH", Website = "https://mueller" + DomainTail, Domain = "mueller" + DomainTail,
            Industry = "Maschinenbau", NaceCode = "01.11", City = "München", Country = "Almanya",
            Address = "Straße der Einheit 5\n80331 München", FitScore = 40, Score = 55, CreatedAt = U(2026, 11, 3)
        };
        foreign.Contacts.Add(new Contact { Name = "Jürgen Weiß", Title = "Leiter IT / CIO", Email = "j.weiss+sap@mueller" + DomainTail,
            EmailStatus = EmailStatus.Risky, Phone = "+49 (0)89 123-456", Location = "München, Deutschland", Source = ContactSource.ApolloSearch,
            ApolloId = "5f0e9d8c7b6a5f4e3d2c1b0a", TitleScore = 90 });
        foreign.Contacts.Add(new Contact { Name = "Ak***y", Title = "ERP Manager", Source = ContactSource.ApolloSearch, ApolloId = "aaaabbbbccccddddeeeeffff", TitleScore = 80 });

        var dirty = new Company
        {
            Name = $"{Tag} 4 =Kirli; \"Veri\", Ltd.", Website = "https://kirli" + DomainTail, Domain = "kirli" + DomainTail,
            Industry = "+Plastik; enjeksiyon", NaceCode = "22.29", City = "-İzmir", Country = "Türkiye",
            Address = "Atatürk Cd. No:5;\tKat 2\r\nBornova \u0007", FitScore = 0, FitReason = new string('ç', 500),
            Description = "Emoji 🚀 Arapça مرحبا Çince 你好", AiAnalysis = "{\"products\":[],\"reason\":null}", RatingJson = null, Score = 12, CreatedAt = U(2026, 11, 4)
        };
        dirty.Contacts.Add(new Contact { Name = "@Ali \"Veli\"; Kaya", Title = "IT; Müdür, Satın Alma\nSorumlusu", Email = "ali.kaya@kirli" + DomainTail,
            Phone = "00123", Location = "İzmir", Headline = "=SUM(A1:A2)", Source = ContactSource.Website, TitleScore = 70 });
        dirty.Leads.Add(new Lead { Contact = dirty.Contacts[0], Score = 12, Status = LeadStatus.Ilgilenmedi, Notes = new string('n', 2000), CreatedAt = U(2026, 11, 5) });

        var disq = new Company { Name = $"{Tag} 5 Elenmiş Firma", Website = "https://elen" + DomainTail, Domain = "elen" + DomainTail,
            City = "Bursa", Country = "Türkiye", Score = 5, EvaluationStatus = EvaluationStatus.Disqualified, EvaluationNote = "Hizmet firması", CreatedAt = U(2026, 11, 6) };
        var notEval = new Company { Name = $"{Tag} 6 İncelenemedi", Website = "https://hata" + DomainTail, Domain = "hata" + DomainTail,
            Score = 0, EvaluationStatus = EvaluationStatus.NotEvaluated, ProcessingError = "Site açılmadı (zaman aşımı)", CreatedAt = U(2026, 11, 7) };

        var many = new Company { Name = $"{Tag} 7 Çok Kişili Holding", Website = "https://cok" + DomainTail, Domain = "cok" + DomainTail,
            City = "İstanbul", Country = "Türkiye", NaceCode = "46.69", Score = 70, FitScore = 75, CreatedAt = U(2026, 11, 8) };
        for (var i = 1; i <= 15; i++)
            many.Contacts.Add(new Contact { Name = $"Kişi {i:00}", Title = i % 3 == 0 ? "CFO" : i % 3 == 1 ? "IT Uzmanı" : "Satın Alma Müdürü",
                Email = $"kisi{i:00}@cok{DomainTail}", EmailStatus = (EmailStatus)(i % 4), Phone = $"05{i:00} 000 00 {i:00}",
                Source = (ContactSource)(i % 3), TitleScore = 100 - i, Location = i % 2 == 0 ? "İstanbul" : null });
        many.Leads.Add(new Lead { Contact = many.Contacts[0], Score = 70, Status = LeadStatus.Ilgilendi, RepliedAt = U(2026, 11, 24), CreatedAt = U(2026, 11, 9) });

        var all = new List<Company> { full, minimal, foreign, dirty, disq, notEval, many };
        foreach (var p in all.SelectMany(c => c.Contacts).Where(p => p.EmailStatus != EmailStatus.Unchecked)) p.EmailCheckedAt = U(2026, 11, 24);
        return all;
    }


    /// <summary>Veritabanına kaydedilmiş gibi kimlik ve bağlar verilir.</summary>
    public static List<Company> Stored()
    {
        var list = Build();
        int cid = 1, pid = 1, lid = 1;
        foreach (var c in list)
        {
            c.Id = cid++;
            foreach (var p in c.Contacts) { p.Id = pid++; p.CompanyId = c.Id; p.Company = c; }
            foreach (var l in c.Leads) { l.Id = lid++; l.CompanyId = c.Id; l.Company = c; l.ContactId = l.Contact?.Id; }
        }
        return list;
    }

    public static List<string> CheckAll(List<Company> companies)
    {
        var problems = new List<string>();
        CheckWorkbook(ExportService.CompanyTables(companies), "Liste", problems);
        foreach (var c in companies) CheckWorkbook(ExportService.CompanyDetailTables(c), "Detay " + c.Name, problems);
        var leads = companies.SelectMany(c => c.Leads).ToList();
        CheckWorkbook(new List<ExportTable> { ExportService.LeadTable(leads, DateTime.UtcNow) }, "Leadler", problems);
        // Anlam kontrolü: başlık adına göre sütun bulunur, içinde gerçekten o alanın değeri olmalı.
        var listTables = ExportService.CompanyTables(companies);
        using (var wb = new XLWorkbook(new MemoryStream(ExportService.ToXlsx(listTables))))
        {
            var firms = Reader(wb.Worksheet(1), listTables[0]);
            foreach (var (c, i) in companies.Select((c, i) => (c, i + 2)))
            {
                firms(i, "Firma", c.Name); firms(i, "Web sitesi", c.Website); firms(i, "Adres", c.Address); firms(i, "Şehir", c.City);
                firms(i, "Ülke", c.Country); firms(i, "NACE", c.NaceCode); firms(i, "Puan", c.Score.ToString());
                firms(i, "Uygunluk (%)", c.FitScore?.ToString()); firms(i, "Neden", c.FitReason);
                firms(i, "Kişi sayısı", c.Contacts.Count.ToString()); firms(i, "Lead sayısı", c.Leads.Count.ToString());
            }
            var people = Reader(wb.Worksheet(2), listTables[1]);
            var row = 2;
            foreach (var c in companies)
                foreach (var p in c.Contacts)
                {
                    people(row, "Firma", c.Name); people(row, "Ad soyad", p.Name); people(row, "Ünvan", p.Title); people(row, "E-posta", p.Email);
                    people(row, "Telefon", p.Phone); people(row, "Konum", p.Location); people(row, "Kaynak", ExportService.SourceLabel(p.Source));
                    people(row, "Profil", p.SourceUrl); row++;
                }
        }
        foreach (var c in companies)
        {
            var detail = ExportService.CompanyDetailTables(c);
            using var wb = new XLWorkbook(new MemoryStream(ExportService.ToXlsx(detail)));
            var people = Reader(wb.Worksheet(2), detail[1]);
            var row = 2;
            foreach (var p in c.Contacts.OrderByDescending(p => p.TitleScore).ThenBy(p => p.Id))
            {
                people(row, "Ad soyad", p.Name); people(row, "Ünvan", p.Title); people(row, "E-posta", p.Email); people(row, "Telefon", p.Phone);
                people(row, "Konum", p.Location); people(row, "Başlık", p.Headline); people(row, "Profil bağlantısı", p.SourceUrl); row++;
            }
            var leadSheet = Reader(wb.Worksheet(3), detail[2]);
            row = 2;
            foreach (var l in c.Leads.OrderByDescending(l => l.CreatedAt))
            {
                leadSheet(row, "Kişi", l.Contact?.Name); leadSheet(row, "Ünvan", l.Contact?.Title); leadSheet(row, "E-posta", l.Contact?.Email);
                leadSheet(row, "Puan", l.Score.ToString()); leadSheet(row, "Notlar", l.Notes); row++;
            }
        }
        var leadTable = ExportService.LeadTable(leads, DateTime.UtcNow);
        using (var wb = new XLWorkbook(new MemoryStream(ExportService.ToXlsx(new[] { leadTable }))))
        {
            var sheet = Reader(wb.Worksheet(1), leadTable);
            foreach (var (l, i) in leads.Select((l, i) => (l, i + 2)))
            {
                sheet(i, "Firma", l.Company?.Name); sheet(i, "Kişi", l.Contact?.Name); sheet(i, "Ünvan", l.Contact?.Title);
                sheet(i, "E-posta", l.Contact?.Email); sheet(i, "Telefon", l.Contact?.Phone); sheet(i, "Şehir", l.Company?.City);
                sheet(i, "Notlar", l.Notes);
            }
        }

        Action<int, string, string?> Reader(IXLWorksheet ws, ExportTable t) => (r, header, value) =>
        {
            var col = Array.IndexOf(t.Headers, header);
            if (col < 0) { problems.Add($"{t.Name}: '{header}' başlığı yok"); return; }
            var got = ws.Cell(r, col + 1).GetString().Replace("\r\n", "\n");
            var want = ExportService.Clean(value).Replace("\r\n", "\n");
            if (got != want) problems.Add($"{t.Name} satır {r} '{header}': '{Short(got)}' ≠ '{Short(want)}'");
        };

        var table = listTables[0];
        var records = CsvRecords(System.Text.Encoding.UTF8.GetString(ExportService.ToCsv(table)).TrimStart((char)0xFEFF));
        if (records.Count != companies.Count + 1) problems.Add($"CSV kayıt sayısı {records.Count - 1} ≠ {companies.Count}");
        foreach (var (r, i) in records.Select((r, i) => (r, i)))
            if (r.Count != table.Headers.Length) problems.Add($"CSV satır {i}: {r.Count} alan ≠ {table.Headers.Length}");
        return problems;
    }

    [Fact]
    public void Her_turden_veride_tum_disa_aktarimlar_hucre_hucre_dogru()
    {
        Assert.Empty(CheckAll(Stored()));
    }

    [Fact]
    public void Kisi_kaynagi_ve_iletisim_bilgisi_dogru_sutunda_okunur_adla()
    {
        var full = Stored()[0];
        using var wb = new XLWorkbook(new MemoryStream(ExportService.ToXlsx(ExportService.CompanyDetailTables(full))));
        var people = wb.Worksheet(2);
        string Cell(int row, string header) => people.Cell(row, Array.IndexOf(ExportService.CompanyDetailTables(full)[1].Headers, header) + 1).GetString();
        Assert.Equal("Ayşe Yılmaz", Cell(2, "Ad soyad"));
        Assert.Equal("Bilgi İşlem Müdürü", Cell(2, "Ünvan"));
        Assert.Equal("ayse.yilmaz@tam" + DomainTail, Cell(2, "E-posta"));
        Assert.Equal("0532 111 22 33", Cell(2, "Telefon"));
        Assert.Equal("Apollo", Cell(2, "Kaynak"));
        Assert.Equal("LinkedIn + Apollo", Cell(3, "Kaynak"));
        Assert.Equal("Firma sitesi", Cell(4, "Kaynak"));
        Assert.Equal("02365550000", Cell(4, "Telefon"));
    }

    public static void CheckWorkbook(List<ExportTable> tables, string label, List<string> problems)
    {
        using var wb = new XLWorkbook(new MemoryStream(ExportService.ToXlsx(tables)));
        for (var t = 0; t < tables.Count; t++)
        {
            var table = tables[t];
            var sheet = wb.Worksheet(t + 1);
            var used = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (used > table.Headers.Length) problems.Add($"{label}/{table.Name}: {used} sütun dolu, başlık {table.Headers.Length}");
            for (var r = 0; r < table.Rows.Count; r++)
                for (var c = 0; c < table.Headers.Length; c++)
                {
                    var want = Expect(table.Rows[r][c]);
                    var cell = sheet.Cell(r + 2, c + 1);
                    var got = cell.DataType == XLDataType.DateTime ? cell.GetDateTime().ToString("dd.MM.yyyy HH:mm") : cell.GetString();
                    if (cell.HasFormula) problems.Add($"{label}/{table.Name} satır {r + 2} '{table.Headers[c]}': formül oldu");
                    if (got.Replace("\r\n", "\n") != want.Replace("\r\n", "\n"))
                        problems.Add($"{label}/{table.Name} satır {r + 2} '{table.Headers[c]}': '{Short(got)}' ≠ '{Short(want)}'");
                }
        }
    }

    private static string Expect(object? v) => v switch
    {
        null => "",
        DateTime d => d.ToString("dd.MM.yyyy HH:mm"),
        int i => i.ToString(),
        _ => ExportService.Clean(EgebisLeadFinder.Localization.Loc.T(v.ToString() ?? ""))
    };

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
    private static string Short(string s) => s.Length > 60 ? s[..60] + "…" : s.Replace("\n", "⏎").Replace("\r", "");

    public static List<List<string>> CsvRecords(string s)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var f = new System.Text.StringBuilder(); var q = false;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (q) { if (ch == '"' && i + 1 < s.Length && s[i + 1] == '"') { f.Append('"'); i++; } else if (ch == '"') q = false; else f.Append(ch); }
            else if (ch == '"') q = true;
            else if (ch == ';') { row.Add(f.ToString()); f.Clear(); }
            else if (ch == '\r') { }
            else if (ch == '\n') { row.Add(f.ToString()); f.Clear(); rows.Add(row); row = new(); }
            else f.Append(ch);
        }
        if (f.Length > 0 || row.Count > 0) { row.Add(f.ToString()); rows.Add(row); }
        return rows;
    }
}
