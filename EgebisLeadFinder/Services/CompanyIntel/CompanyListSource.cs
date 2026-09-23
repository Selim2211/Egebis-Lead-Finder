using System.Text.Json;
using System.Text.Json.Serialization;
using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Services.CompanyIntel;

/// <summary>
/// Firmayi İSO 500 / İSO 1000, Capital 500, Fortune 500 Türkiye ve TİM 1000
/// (ihracatci) referans listeleriyle eslestirir. Eslesirse "buyuk olcekli /
/// koklu / ihracatci" sinyali uretir.
///
/// Veri koda gomulu degil: Data/reference/company-lists.json dosyasindan okunur
/// (kara liste / skor agirliklari gibi elle guncellenir). Dosya yoksa kaynak
/// sessizce bos doner.
/// </summary>
public class CompanyListSource : ICompanyIntelSource
{
    private readonly ResearchOptions _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<CompanyListSource> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public CompanyListSource(
        IOptions<ResearchOptions> options,
        IHostEnvironment env,
        ILogger<CompanyListSource> logger)
    {
        _options = options.Value;
        _env = env;
        _logger = logger;
    }

    public string Name => "Sektör listeleri (İSO 500 / Capital 500 / TİM 1000)";

    public async Task<SourceIntel> CollectAsync(Company company, CancellationToken ct = default)
    {
        var intel = new SourceIntel { SourceName = Name };
        var name = (company.Name ?? string.Empty).Trim();
        if (name.Length == 0) return intel;

        var data = await LoadAsync(ct);
        if (data is null) return intel;

        var match = data.Companies.FirstOrDefault(c =>
            CompanyNameMatch.IsMatch(name, c.Name) ||
            c.Aliases.Any(a => CompanyNameMatch.IsMatch(name, a)));

        if (match is null)
        {
            _logger.LogInformation("{Company}: sektör listelerinde bulunamadı.", name);
            return intel;
        }

        foreach (var m in match.Memberships)
        {
            var year = m.Year > 0 ? $" ({m.Year})" : "";
            var band = string.IsNullOrWhiteSpace(m.Band) ? "" : $" — {m.Band}";
            var note = string.IsNullOrWhiteSpace(m.Note) ? "" : $". {m.Note}";

            intel.Snippets.Add(new IntelSnippet
            {
                Text = $"{match.Name}, {m.List}{year} listesinde yer alıyor{band}{note}.",
                Kind = KindFor(m.List)
            });
        }

        return intel;
    }

    private static IntelKind KindFor(string list) =>
        list.Contains("TİM", StringComparison.OrdinalIgnoreCase) ||
        list.Contains("ihracat", StringComparison.OrdinalIgnoreCase)
            ? IntelKind.Buyume
            : IntelKind.Finansal;

    private async Task<ListData?> LoadAsync(CancellationToken ct)
    {
        var path = Path.IsPathRooted(_options.CompanyListDataPath)
            ? _options.CompanyListDataPath
            : Path.Combine(_env.ContentRootPath, _options.CompanyListDataPath);

        if (!File.Exists(path))
        {
            _logger.LogInformation("Sektör listesi dosyası yok: {Path}", path);
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ListData>(stream, JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sektör listesi dosyası okunamadı: {Path}", path);
            return null;
        }
    }

    // --- JSON semasi (Turkce alan adlari) ---

    private class ListData
    {
        [JsonPropertyName("firmalar")]
        public List<ListCompany> Companies { get; set; } = new();
    }

    private class ListCompany
    {
        [JsonPropertyName("ad")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("diğer_adlar")]
        public List<string> Aliases { get; set; } = new();

        [JsonPropertyName("üyelikler")]
        public List<Membership> Memberships { get; set; } = new();
    }

    private class Membership
    {
        [JsonPropertyName("liste")]
        public string List { get; set; } = string.Empty;

        [JsonPropertyName("yıl")]
        public int Year { get; set; }

        [JsonPropertyName("bant")]
        public string? Band { get; set; }

        [JsonPropertyName("not")]
        public string? Note { get; set; }
    }
}
