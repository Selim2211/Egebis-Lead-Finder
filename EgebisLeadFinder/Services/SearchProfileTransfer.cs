using System.Globalization;
using ClosedXML.Excel;
using EgebisLeadFinder.Data;
using EgebisLeadFinder.Models;

namespace EgebisLeadFinder.Services;

/// <summary>
/// Arama profillerini Excel'e aktarir / Excel'den ice alir. Dosya, hem disa aktarimin hem de "şablon indir"in
/// ciktisidir: "Profiller" sayfasi (1. satir baslik) ve "Açıklama" sayfasi. Ice aktarim ayni sayfayi okur;
/// disa aktarilan dosya hicbir bilgi kaybetmeden geri yuklenebilir (cok ulke ve urunler dahil).
/// </summary>
public static class SearchProfileTransfer
{
    public const string DataSheet = "Profiller";
    public const string HelpSheet = "Açıklama";
    public const int MaxRows = 50;
    public const long MaxFileBytes = 1_000_000;

    public static readonly string[] Headers =
        { "Profil adı", "Sektör", "NACE kodu", "Ürünler", "Ülke / bölge", "İl / bölge", "En fazla firma", "Herkese açık" };

    // Sutun sirasi (Headers ile ayni). Ice aktarimda sutunlar BASLIK ADIYLA bulunur: eski (Ürünler sutunu olmayan)
    // dosyalar ve sutunlari yer degistirmis dosyalar da dogru okunur.
    private const int ColName = 1, ColIndustry = 2, ColNace = 3, ColProducts = 4, ColRegion = 5, ColCity = 6, ColMax = 7, ColShared = 8;

    /// <summary>Profillerden Excel dosyasi uretir; profil yoksa (sablon) ornek satirlar konur.</summary>
    public static byte[] Build(IEnumerable<SearchProfile> profiles, bool template)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(DataSheet);

        // Metin sutunlari: Excel kullanicinin yazdigi "27.10" ya da "01.11"i sayiya cevirip bozmasin.
        foreach (var col in new[] { ColName, ColIndustry, ColNace, ColProducts, ColRegion, ColCity, ColShared })
            ws.Column(col).Style.NumberFormat.Format = "@";

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
            Put(ws, row++, "Ege Otomotiv", "Otomotiv yan sanayi", "", "", "Türkiye", "İzmir, Manisa", 32, "Hayır");
            Put(ws, row++, "Bavyera Makina", "Makine imalatı", "28.99", "", "Almanya", "Bayern", null, "Hayır");
            Put(ws, row++, "Texas Plastik", "Plastik ürünler", "22", "", "Amerika Birleşik Devletleri", "Texas, Ohio", 16, "Evet");
            Put(ws, row++, "Avrupa Beyaz Eşya", "", "27.51", "buzdolabı, fırın", "Almanya, Fransa, İtalya", "", 30, "Evet");
        }
        else
        {
            foreach (var p in profiles)
            {
                var regions = RegionsOf(p);
                // Cok ulkeli profilde il/bolge saklanmaz (her ulkenin geneli aranir).
                Put(ws, row++, p.Name, p.Industry, p.NaceCode, p.NaceProducts, string.Join(", ", regions.Select(r => r.Name)),
                    regions.Count > 1 ? null : p.City, p.MaxCompanies, p.IsPublic ? "Evet" : "Hayır");
            }
        }

        ws.Columns().AdjustToContents(1, Math.Max(row, 8), 8, 45);
        ws.SheetView.FreezeRows(1);

        var help = wb.AddWorksheet(HelpSheet);
        var lines = new (string, string)[]
        {
            ("Nasıl kullanılır?", "\"Profiller\" sayfasına her satıra bir arama profili yazın, dosyayı kaydedip Arama Profilleri ekranından \"İçe aktar\" ile yükleyin. 1. satır başlıktır, değiştirmeyin; örnek satırları silin veya değiştirin."),
            ("Profil adı *", "Zorunlu. En fazla 150 karakter. Sizde aynı adlı profil varsa o satır atlanır (var olan profil değişmez)."),
            ("Sektör *", "Zorunlu (NACE kodu yazmıyorsanız). Aranacak sektör, ör. \"Otomotiv yan sanayi\". En fazla 150 karakter."),
            ("NACE kodu", "İsteğe bağlı. \"27\", \"27.51\" ya da \"27.51.08\" biçiminde. Yazarsanız arama bu koda göre yapılır; geçerli bir NACE kodu olmalı. Hücre metin biçimindedir, kodu olduğu gibi yazın."),
            ("Ürünler", "İsteğe bağlı. NACE aramasında öne çıkacak ürünler; virgülle ayırın, ör. \"buzdolabı, fırın\". En fazla 1000 karakter."),
            ("Ülke / bölge", $"İsteğe bağlı; boşsa {SearchRegions.Default.Name}. Bir ya da birden çok ülke (virgülle ayırın, en fazla {SearchRegions.MaxCountriesPerSearch}); ülke adı ya da kısa kodu: {string.Join(", ", SearchRegions.All.Select(r => $"{r.Name} ({r.Key})"))}."),
            ("İl / bölge", "İsteğe bağlı ve yalnızca tek ülke yazıldığında. Virgülle ayırın, ör. \"İzmir, Manisa\" ya da ABD için \"Texas, Ohio\". Boşsa ülkenin tamamı aranır."),
            ("En fazla firma", $"İsteğe bağlı, 1 ile {SettingKeys.SearchMaxCompaniesUpperLimit} arası bir tam sayı. Boşsa Ayarlar'daki varsayılan kullanılır."),
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

    /// <summary>Profilin ulkeleri: cok ulkeli ise hepsi (secim sirasiyla), degilse tek (ana) ulke.</summary>
    public static List<SearchRegion> RegionsOf(SearchProfile p)
    {
        var many = SearchRegions.ParseKeys(p.RegionKeys);
        if (many.Count > 1) return many;
        return new List<SearchRegion>
        {
            !string.IsNullOrWhiteSpace(p.RegionKey) ? SearchRegions.Get(p.RegionKey) : SearchRegions.ByCountry(p.Country) ?? SearchRegions.Default
        };
    }

    private static void Put(IXLWorksheet ws, int row, string? name, string? industry, string? nace, string? products, string? region,
        string? city, int? max, string shared)
    {
        ws.Cell(row, ColName).SetValue(ExportService.Clean(name));
        ws.Cell(row, ColIndustry).SetValue(ExportService.Clean(industry));
        ws.Cell(row, ColNace).SetValue(nace ?? string.Empty);
        ws.Cell(row, ColProducts).SetValue(ExportService.Clean(products));
        ws.Cell(row, ColRegion).SetValue(region ?? string.Empty);
        ws.Cell(row, ColCity).SetValue(ExportService.Clean(city));
        if (max is int m) ws.Cell(row, ColMax).Value = m;
        ws.Cell(row, ColShared).SetValue(shared);
    }

    /// <summary>Ice aktarilan bir satirin sonucu: ya gecerli profil verisi ya da hata.</summary>
    public sealed record Row(int LineNo, string? Name, string? Industry, string? Nace, string? RegionKey, string? Country,
        string? City, int? MaxCompanies, bool IsPublic, string? Error, string? Products = null, string? RegionKeys = null);

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

            var header = Text(ws.Cell(1, 1));
            if (!string.Equals(header, Headers[0], StringComparison.OrdinalIgnoreCase))
                return new ParseResult(new List<Row>(), "İlk sütun başlığı \"Profil adı\" olmalı; şablonu indirip onun üzerinde doldurun.");

            // Sutunlar baslik adiyla bulunur.
            var lastCol = Math.Max(ws.LastColumnUsed()?.ColumnNumber() ?? 1, Headers.Length);
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var c = 1; c <= lastCol; c++)
            {
                var h = Text(ws.Cell(1, c));
                if (h.Length > 0) byName.TryAdd(h, c);
            }
            var cols = Headers.Select(h => byName.TryGetValue(h, out var c) ? c : (int?)null).ToArray();
            if (cols[ColIndustry - 1] is null && cols[ColNace - 1] is null)
                return new ParseResult(new List<Row>(), "\"Sektör\" ya da \"NACE kodu\" başlığı bulunamadı; şablonu indirip onun üzerinde doldurun.");

            var last = ws.LastRowUsed()?.RowNumber() ?? 1;
            var rows = new List<Row>();
            for (var r = 2; r <= last; r++)
            {
                var rowNo = r;
                string Cell(int index) => cols[index - 1] is int c ? Text(ws.Cell(rowNo, c)) : string.Empty;
                var nace = cols[ColNace - 1] is int nc ? NaceText(ws.Cell(rowNo, nc)) : string.Empty;
                if (Enumerable.Range(1, Headers.Length).All(i => Cell(i).Length == 0)) continue;
                if (rows.Count >= MaxRows)
                    return new ParseResult(rows, $"En fazla {MaxRows} satır içe aktarılabilir; fazlası yok sayıldı.");
                rows.Add(ParseRow(rowNo, Cell(ColName), Cell(ColIndustry), nace, Cell(ColRegion), Cell(ColCity), Cell(ColMax),
                    Cell(ColShared), Cell(ColProducts)));
            }
            return new ParseResult(rows, null);
        }
    }

    /// <summary>Hucre metni; hata degerli (#N/A) ya da okunamayan hucre bos sayilir. Gorunmez karakterler ve bolunmez bosluk temizlenir.</summary>
    private static string Text(IXLCell cell)
    {
        try
        {
            // Formul hucresinde hesaplanmis deger okunur; hata (#N/A, #DEĞER!) bos sayilir.
            var v = cell.Value;
            if (v.IsError || v.IsBlank) return string.Empty;
            var value = v.IsNumber ? v.GetNumber().ToString("0.##########", CultureInfo.InvariantCulture)
                : v.IsBoolean ? (v.GetBoolean() ? "true" : "false")
                : v.IsDateTime ? v.GetDateTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                : v.ToString(CultureInfo.InvariantCulture);
            if (value.StartsWith('#') && value.EndsWith('!') || value is "#N/A") return string.Empty;
            return ExportService.Clean(value).Replace(' ', ' ').Trim();
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// NACE hucresi Excel'de sayiya donmus olabilir: 1.11 → "01.11" (bolum iki hanelidir), 27 → "27".
    /// Metin olarak yazilmissa oldugu gibi okunur.
    /// </summary>
    private static string NaceText(IXLCell cell)
    {
        try
        {
            var v = cell.Value;
            if (!v.IsNumber) return Text(cell);
            var value = v.GetNumber();
            if (value < 0 || value >= 100) return Text(cell);
            var parts = value.ToString("0.####", CultureInfo.InvariantCulture).Split('.');
            return parts[0].PadLeft(2, '0') + (parts.Length > 1 ? "." + parts[1] : "");
        }
        catch { return Text(cell); }
    }

    public static Row ParseRow(int line, string name, string industry, string nace, string region, string city, string max, string shared,
        string products = "")
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
        if (products.Length > 1000) return Fail("Ürünler 1000 karakterden uzun.");

        // Bir ya da birden cok ulke: "Almanya, Fransa" / "DE; FR". Tekrarlar bir kez sayilir, sira korunur.
        var regions = new List<SearchRegion>();
        foreach (var part in region.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = SearchRegions.All.FirstOrDefault(r =>
                string.Equals(r.Key, part, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Name, part, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Country, part, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Fold(r.Name), Fold(part), StringComparison.Ordinal));
            if (match is null) return Fail($"Ülke / bölge tanınmadı: {part}");
            if (regions.All(r => r.Key != match.Key)) regions.Add(match);
        }
        if (regions.Count == 0) regions.Add(SearchRegions.Default);
        if (regions.Count > SearchRegions.MaxCountriesPerSearch)
            return Fail($"Bir profilde en fazla {SearchRegions.MaxCountriesPerSearch} ülke olabilir ({regions.Count} yazıldı).");
        if (regions.Count > 1 && city.Length > 0)
            return Fail("İl / bölge yalnızca tek ülke yazıldığında kullanılır; birden çok ülkede boş bırakın.");

        int? maxCompanies = null;
        if (max.Length > 0)
        {
            if (!decimal.TryParse(max.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
                || d != Math.Floor(d) || d < 1 || d > SettingKeys.SearchMaxCompaniesUpperLimit)
                return Fail($"En fazla firma 1-{SettingKeys.SearchMaxCompaniesUpperLimit} arası bir tam sayı olmalı.");
            maxCompanies = (int)d;
        }

        var isPublic = Fold(shared) is "evet" or "e" or "yes" or "y" or "true" or "dogru" or "1" or "x";
        var cityText = city.Length == 0 || regions.Count > 1 ? null : (city.Length > 1000 ? city[..1000] : city);
        var keys = regions.Count > 1 ? string.Join(",", regions.Select(r => r.Key)) : null;

        return new Row(line, name, industry.Length == 0 ? null : industry, naceCode, regions[0].Key, regions[0].Country,
            cityText, maxCompanies, isPublic, null, products.Length == 0 ? null : products, keys);
    }

    /// <summary>Turkce harf ve buyuk/kucuk farki gozetmeden karsilastirma icin: "İTALYA" = "italya", "Doğru" = "dogru".</summary>
    private static string Fold(string s)
    {
        var lower = s.Trim().Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant();
        return lower.Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
    }
}
