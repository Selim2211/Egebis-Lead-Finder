using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

public class CompanyListSourceTests : IDisposable
{
    private readonly string _dir;

    private const string Data = """
        {
          "firmalar": [
            {
              "ad": "Ford Otomotiv Sanayi A.Ş.",
              "diğer_adlar": [ "Ford Otosan" ],
              "üyelikler": [
                { "liste": "İSO 500", "yıl": 2023, "bant": "ilk 3", "not": "en büyük sanayi kuruluşlarından" },
                { "liste": "TİM 1000", "yıl": 2023, "bant": "ilk 5", "not": "ihracat lideri" }
              ]
            }
          ]
        }
        """;

    public CompanyListSourceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "eglf-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "Data", "reference"));
        File.WriteAllText(Path.Combine(_dir, "Data", "reference", "company-lists.json"), Data);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private CompanyListSource Create(string? path = null) =>
        new(Options.Create(new ResearchOptions
            {
                CompanyListDataPath = path ?? "Data/reference/company-lists.json"
            }),
            new StubEnv(_dir),
            NullLogger<CompanyListSource>.Instance);

    [Fact]
    public async Task Listede_olan_firma_uyelik_snippetleri_uretir()
    {
        var intel = await Create().CollectAsync(new Company { Name = "Ford Otosan" });

        Assert.Equal(2, intel.Snippets.Count);
        Assert.Contains(intel.Snippets, s => s.Text.Contains("İSO 500") && s.Kind == IntelKind.Finansal);
        Assert.Contains(intel.Snippets, s => s.Text.Contains("TİM 1000") && s.Kind == IntelKind.Buyume);
    }

    [Fact]
    public async Task Tam_unvanla_da_eslesir()
    {
        var intel = await Create().CollectAsync(new Company { Name = "Ford Otomotiv Sanayi Anonim Şirketi" });

        Assert.NotEmpty(intel.Snippets);
    }

    [Fact]
    public async Task Listede_olmayan_firma_bos_doner()
    {
        var intel = await Create().CollectAsync(new Company { Name = "Bursa Küçük Döküm Ltd. Şti." });

        Assert.Empty(intel.Snippets);
        Assert.Null(intel.Error);
    }

    [Fact]
    public async Task Dosya_yoksa_sessizce_bos_doner()
    {
        var intel = await Create("Data/reference/yok.json").CollectAsync(new Company { Name = "Ford Otosan" });

        Assert.Empty(intel.Snippets);
        Assert.Null(intel.Error);
    }

    private class StubEnv : IHostEnvironment
    {
        public StubEnv(string root) => ContentRootPath = root;

        public string ApplicationName { get; set; } = "Test";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
