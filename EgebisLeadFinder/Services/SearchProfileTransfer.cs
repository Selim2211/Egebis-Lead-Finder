using ClosedXML.Excel;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Arama profillerini Excel'e aktarir / Excel'den ice alir. Dosya, hem disa aktarimin hem de "şablon indir"in
/// ciktisidir: "Profiller" sayfasi (1. satir baslik) ve "Açıklama" sayfasi. Ice aktarim ayni sayfayi okur.
/// </summary>
public static class SearchProfileTransfer
{
    public const string DataSheet = "Profiller";
    public const string HelpSheet = "Açıklama";
    public const int MaxRows = 50;
    public const long MaxFileBytes = 1_000_000;

    public static readonly string[] Headers =
        { "Profil adı", "Sektör", "NACE kodu", "Ülke / bölge", "İl / bölge", "En fazla firma", "Herkese açık" };

    /// <summary>Profillerden Excel dosyasi uretir; profil yoksa (sablon) ornek satirlar konur.</summary>
    public static byte[] Build(IEnumerable<SearchProfile> profiles, bool template)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(DataSheet);
        for (var i = 0; i < Headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = Headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#E7F0FF");
        }

        var row = 2;
        if (template)
        {
            Put(ws, row++, "Ege Otomotiv", "Otomotiv yan sanayi", "", "Türkiye", "İzmir, Manisa", 32, "Hayır");
            Put(ws, row++, "Bavyera Makina", "Makine imalatı", "28.99", "Almanya", "Bayern", "", "Hayır");
            Put(ws, row++, "Texas Plastik", "Plastik ürünler", "22", "Amerika Birleşik Devletleri", "Texas, Ohio", 16, "Evet");
        }
        else
        {
            foreach (var p in profiles)
            {
                var region = !string.IsNullOrWhiteSpace(p.RegionKey) ? SearchRegions.Get(p.RegionKey) : SearchRegions.ByCountry(p.Country) ?? SearchRegions.Default;
                Put(ws, row++, p.Name, p.Industry, p.NaceCode, region.Name, p.City, p.MaxCompanies, p.IsPublic ? "Evet" : "Hayır");
            }
        }

        ws.Column(5).Style.NumberFormat.Format = "@";
        ws.Columns().AdjustToContents(1, Math.Max(row, 8), 8, 45);
        ws.SheetView.FreezeRows(1);

        var help = wb.AddWorksheet(HelpSheet);
        var lines = new (string, string)[]
        {
            ("Nasıl kullanılır?", "\"Profiller\" sayfasına her satıra bir arama profili yazın, dosyayı kaydedip Arama Profilleri ekranından \"İçe aktar\" ile yükleyin. 1. satır başlıktır, değiştirmeyin; örnek satırları silin veya değiştirin."),
            ("Profil adı *", "Zorunlu. En fazla 150 karakter. Sizde aynı adlı profil varsa o satır atlanır (var olan profil değişmez)."),
            ("Sektör *", "Zorunlu (NACE kodu yazmıyorsanız). Aranacak sektör, ör. \"Otomotiv yan sanayi\"."),
            ("NACE kodu", "İsteğe bağlı. \"27\", \"27.51\" ya da \"27.51.08\" biçiminde. Yazarsanız arama bu koda göre yapılır; geçerli bir NACE kodu olmalı."),
            ("Ülke / bölge", $"İsteğe bağlı; boşsa {SearchRegions.Default.Name}. Ülke adı ya da kısa kodu: {string.Join(", ", SearchRegions.All.Select(r => $"{r.Name} ({r.Key})"))}."),
            ("İl / bölge", "İsteğe bağlı. Virgülle ayırın, ör. \"İzmir, Manisa\" ya da ABD için \"Texas, Ohio\". Boşsa ülkenin tamamı aranır."),
            ("En fazla firma", $"İsteğe bağlı, 1 ile {SettingKeys.SearchMaxCompaniesUpperLimit} arası bir sayı. Boşsa Ayarlar'daki varsayılan kullanılır."),
            ("Herkese açık", "\"Evet\" yazarsanız profil tüm kullanıcılara açılır (bulduğunuz firmalar paylaşılmaz). Boş veya \"Hayır\": yalnızca size özel."),
            ("Sınırlar", $"Bir dosyada en fazla {MaxRows} satır ve en fazla 1 MB. Yalnızca .xlsx dosyaları kabul edilir. Hatalı satırlar atlanır, doğru olanlar eklenir; sonuç ekranda listelenir.")
        };
        help.Cell(1, 1).Value = "Alan";
        help.Cell(1, 2).Value = "Açıklama";
        help.Range(1, 1, 1, 2).Style.Font.Bold = true;
        for (var i = 0; i < lines.Length; i++)
        {
            help.Cell(i + 2, 1).Value = lines[i].Item1;
            help.Cell(i + 2, 1).Style.Font.Bold = true;
            help.Cell(i + 2, 2).Value = lines[i].Item2;
        }
        help.Column(1).Width = 20;
        help.Column(2).Width = 110;
        help.Column(2).Style.Alignment.WrapText = true;
        help.Rows().Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Put(IXLWorksheet ws, int row, string? name, string? industry, string? nace, string? region, string? city, object? max, string shared)
    {
        ws.Cell(row, 1).Value = name ?? string.Empty;
        ws.Cell(row, 2).Value = industry ?? string.Empty;
        ws.Cell(row, 3).SetValue(nace ?? string.Empty);
        ws.Cell(row, 4).Value = region ?? string.Empty;
        ws.Cell(row, 5).SetValue(city ?? string.Empty);
        if (max is int m) ws.Cell(row, 6).Value = m; else ws.Cell(row, 6).Value = string.Empty;
        ws.Cell(row, 7).Value = shared;
    }

    /// <summary>Ice aktarilan bir satirin sonucu: ya gecerli profil verisi ya da hata.</summary>
    public sealed record Row(int LineNo, string? Name, string? Industry, string? Nace, string? RegionKey, string? Country,
        string? City, int? MaxCompanies, bool IsPublic, string? Error);

    public sealed record ParseResult(List<Row> Rows, string? FileError);

    public static ParseResult Parse(Stream stream)
    {
        XLWorkbook wb;
        try { wb = new XLWorkbook(stream); }
        catch { return new ParseResult(new List<Row>(), "Dosya okunamadı. Şablondaki gibi bir .xlsx dosyası yükleyin."); }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault(s => string.Equals(s.Name, DataSheet, StringComparison.OrdinalIgnoreCase))
                     ?? wb.Worksheets.FirstOrDefault();
            if (ws is null) return new ParseResult(new List<Row>(), "Dosyada sayfa bulunamadı.");

            var header = ws.Cell(1, 1).GetString().Trim();
            if (!string.Equals(header, Headers[0], StringComparison.OrdinalIgnoreCase))
                return new ParseResult(new List<Row>(), "İlk sütun başlığı \"Profil adı\" olmalı; şablonu indirip onun üzerinde doldurun.");

            var last = ws.LastRowUsed()?.RowNumber() ?? 1;
            var rows = new List<Row>();
            for (var r = 2; r <= last; r++)
            {
                string Cell(int c) => ws.Cell(r, c).GetString().Trim();
                if (Enumerable.Range(1, Headers.Length).All(c => Cell(c).Length == 0)) continue;
                if (rows.Count >= MaxRows)
                    return new ParseResult(rows, $"En fazla {MaxRows} satır içe aktarılabilir; fazlası yok sayıldı.");
                rows.Add(ParseRow(r, Cell(1), Cell(2), Cell(3), Cell(4), Cell(5), Cell(6), Cell(7)));
            }
            return new ParseResult(rows, null);
        }
    }

    public static Row ParseRow(int line, string name, string industry, string nace, string region, string city, string max, string shared)
    {
        Row Fail(string error) => new(line, name, null, null, null, null, null, null, false, error);

        if (name.Length == 0) return Fail("Profil adı boş.");
        if (name.Length > 150) return Fail("Profil adı 150 karakterden uzun.");

        string? naceCode = null;
        if (nace.Length > 0)
        {
            naceCode = NaceCatalog.NormalizeFull(nace);
            if (naceCode is null || !NaceCatalog.Exists(naceCode)) return Fail($"NACE kodu geçersiz: {nace}");
        }
        if (industry.Length == 0 && naceCode is null) return Fail("Sektör boş (sektör ya da NACE kodu yazın).");
        if (industry.Length > 150) return Fail("Sektör 150 karakterden uzun.");

        var regionInfo = region.Length == 0
            ? SearchRegions.Default
            : SearchRegions.All.FirstOrDefault(r =>
                  string.Equals(r.Key, region, StringComparison.OrdinalIgnoreCase)
                  || string.Equals(r.Name, region, StringComparison.OrdinalIgnoreCase)
                  || string.Equals(r.Country, region, StringComparison.OrdinalIgnoreCase));
        if (regionInfo is null) return Fail($"Ülke / bölge tanınmadı: {region}");

        int? maxCompanies = null;
        if (max.Length > 0)
        {
            if (!int.TryParse(max, out var m) || m < 1 || m > SettingKeys.SearchMaxCompaniesUpperLimit)
                return Fail($"En fazla firma 1-{SettingKeys.SearchMaxCompaniesUpperLimit} arası bir sayı olmalı.");
            maxCompanies = m;
        }

        var isPublic = shared.Equals("evet", StringComparison.OrdinalIgnoreCase)
                       || shared.Equals("yes", StringComparison.OrdinalIgnoreCase)
                       || shared.Equals("true", StringComparison.OrdinalIgnoreCase);
        var cityText = city.Length == 0 ? null : (city.Length > 1000 ? city[..1000] : city);

        return new Row(line, name, industry.Length == 0 ? null : industry, naceCode, regionInfo.Key, regionInfo.Country,
            cityText, maxCompanies, isPublic, null);
    }
}
