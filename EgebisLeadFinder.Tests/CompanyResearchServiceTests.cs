using EgebisLeadFinder.Configuration;
using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using EgebisLeadFinder.Services.CompanyIntel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EgebisLeadFinder.Tests;

/// <summary>
/// Orkestrator: kaynaklari calistirir, AI'a verir, evaluator'dan gecirir.
/// Bir kaynak patlasa bile digerleri devam eder; kaynak yoksa temiz "bilinmiyor" doner.
/// </summary>
public class CompanyResearchServiceTests
{
    private static CompanyResearchService Create(
        IEnumerable<ICompanyIntelSource> sources, ICompanyRatingAi ai) =>
        new(sources, ai,
            new CompanyRatingEvaluator(Options.Create(new ResearchOptions())),
            Options.Create(new ResearchOptions()),
            NullLogger<CompanyResearchService>.Instance);

    [Fact]
    public async Task Firma_adi_yoksa_basarisiz()
    {
        var result = await Create(Array.Empty<ICompanyIntelSource>(), new FakeAi())
            .ResearchAsync(new Company { Name = "" });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Hic_snippet_yoksa_bilinmiyor_ama_linkler_yine_gelir()
    {
        var registry = new FakeSource("Resmi kayıtlar", links: new[]
        {
            new IntelLink { Label = "MERSİS", Url = "https://mersis" }
        });

        var result = await Create(new[] { registry }, new FakeAi { ShouldBeCalled = false })
            .ResearchAsync(new Company { Name = "Test A.Ş." });

        Assert.Equal(RatingSignal.Bilinmiyor, result.Evaluation!.Signal);
        Assert.Contains(result.Links, l => l.Label == "MERSİS");
        Assert.NotNull(result.RawJson);
    }

    [Fact]
    public async Task Bir_kaynak_patlarsa_digeri_devam_eder()
    {
        var boom = new FakeSource("Patlayan", throwOnCollect: true);
        var news = new FakeSource("Haber", snippets: new[]
        {
            new IntelSnippet { Text = "Firma yeni yatırım yaptı", Kind = IntelKind.Buyume }
        });

        var ai = new FakeAi
        {
            Rating = new CompanyRating { Signal = "guclu", GrowthSignals = { "yatırım" } }
        };

        var result = await Create(new[] { boom, news }, ai).ResearchAsync(new Company { Name = "Test" });

        Assert.True(result.Success);
        Assert.Contains(result.Sources, s => s.SourceName == "Patlayan" && s.Error != null);
    }

    [Fact]
    public async Task AI_basarisizsa_kismi_kayit_uretilir_kaynaklar_kaybolmaz()
    {
        // AI patlasa da (ör. Gemini 503) toplanan kaynaklar (Serper kredisiyle
        // toplandi) copa atilmamali: kismi bir kayit uretilip kaydedilmeli.
        var news = new FakeSource("Haber", snippets: new[]
        {
            new IntelSnippet { Text = "bir şey", Kind = IntelKind.Haber }
        });

        var result = await Create(new[] { news }, new FakeAi { Fail = "kota doldu" })
            .ResearchAsync(new Company { Name = "Test" });

        Assert.True(result.Success);
        Assert.Contains("kota", result.Rating!.Summary);
        Assert.Contains(result.Rating.CheckedSources, s => s.Name == "Haber");
    }

    [Fact]
    public async Task Nihai_sinyal_evaluatordan_gelir_AI_onerisini_ezmez()
    {
        var news = new FakeSource("Haber", snippets: new[]
        {
            new IntelSnippet { Text = "Şirkete haciz geldi", Kind = IntelKind.Risk }
        });

        var ai = new FakeAi
        {
            Rating = new CompanyRating
            {
                Signal = "guclu",
                RiskSignals =
                {
                    new RiskSignal { Text = "Haciz", Severity = "yuksek", SourceUrl = "https://haber/1" }
                }
            }
        };

        var result = await Create(new[] { news }, ai).ResearchAsync(new Company { Name = "Test" });

        Assert.Equal(RatingSignal.Riskli, result.Evaluation!.Signal);
        Assert.Equal("riskli", result.Rating!.Signal);
    }

    // ----- Sahteler -----

    private class FakeSource : ICompanyIntelSource
    {
        private readonly IntelSnippet[] _snippets;
        private readonly IntelLink[] _links;
        private readonly bool _throw;

        public FakeSource(string name, IntelSnippet[]? snippets = null, IntelLink[]? links = null,
            bool throwOnCollect = false)
        {
            Name = name;
            _snippets = snippets ?? Array.Empty<IntelSnippet>();
            _links = links ?? Array.Empty<IntelLink>();
            _throw = throwOnCollect;
        }

        public string Name { get; }

        public Task<SourceIntel> CollectAsync(Company company, CancellationToken ct = default)
        {
            if (_throw) throw new InvalidOperationException("kaynak patladı");

            var intel = new SourceIntel { SourceName = Name };
            intel.Snippets.AddRange(_snippets);
            intel.Links.AddRange(_links);
            return Task.FromResult(intel);
        }
    }

    private class FakeAi : ICompanyRatingAi
    {
        public CompanyRating? Rating { get; set; }
        public string? Fail { get; set; }
        public bool ShouldBeCalled { get; set; } = true;

        public Task<CompanyRatingAiResult> RateCompanyAsync(CompanyRatingInput input, CancellationToken ct = default)
        {
            Assert.True(ShouldBeCalled, "AI çağrılmamalıydı");

            if (Fail is not null)
                return Task.FromResult(CompanyRatingAiResult.Failed(Fail));

            var rating = Rating ?? new CompanyRating();
            return Task.FromResult(new CompanyRatingAiResult
            {
                Rating = rating,
                RawJson = "{}"
            });
        }
    }
}
