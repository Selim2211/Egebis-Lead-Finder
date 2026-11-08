using EgebisLeadFinder.Models;
using EgebisLeadFinder.Services;
using Xunit;

namespace EgebisLeadFinder.Tests;

public class ProducerSignalsTests
{
    // ---- Elle etiketlenmis ornek site metinleri (olcu seti). Gercek sitelerin dilini taklit eder; yeni bir yanlis karar
    // ---- gorulurse buraya ornek eklenir, boylece kural degisikligi once bu sette olculur.
    public static IEnumerable<object[]> Gold => new[]
    {
        // ---- Uretici
        Case(ProducerKind.Producer, "bulasik makinesi fabrikasi",
            "Hakkımızda. Firmamız 1987 yılından beri bulaşık makinesi üretmektedir. 35.000 m² kapalı alanda kurulu fabrikamızda yıllık 400.000 adet üretim kapasitemiz bulunmaktadır."),
        Case(ProducerKind.Producer, "plastik ambalaj",
            "Plastik ambalaj sektöründe 3.500 m² fabrikamızda yıllık 5.000 ton üretim yapıyoruz. Enjeksiyon ve ekstrüzyon makine parkımız 40 makineden oluşur."),
        Case(ProducerKind.Producer, "celik boru",
            "Endüstriyel boru üretiminde ISO 9001 belgeli firmamız, Gebze OSB'deki üretim tesislerimizde çelik boru imal etmektedir. Kapasite raporumuz mevcuttur."),
        Case(ProducerKind.Producer, "kalip",
            "Kalıp ve plastik enjeksiyon parçaları üretiyoruz.\n12.000 m² üretim alanı, 60 tezgâh."),
        Case(ProducerKind.Producer, "marka sahibi bayi agi ile",
            "Bayilerimiz Türkiye'nin her yerinde. Ürünlerimizi kendi fabrikamızda üretiyoruz."),
        Case(ProducerKind.Producer, "uretim hatti",
            "Üretim hattımız saatte 2.000 adet kapasiteyle çalışır."),
        Case(ProducerKind.Producer, "kapasite raporu ve fason",
            "Sanayi sicil belgemiz ve kapasite raporumuzla 2005'ten beri fason üretim yapıyoruz."),

        // ---- Bayi / ithalatci / e-ticaret
        Case(ProducerKind.Trader, "yetkili bayi",
            "Arçelik ve Bosch markalarının Ege bölgesi yetkili bayisiyiz. Beyaz eşya satış ve servis hizmeti."),
        Case(ProducerKind.Trader, "ithalatci distributor",
            "Çin ve Tayvan'dan ithal ediyoruz, Türkiye distribütörüyüz. Markalarımız: Alfa, Beta, Gama."),
        Case(ProducerKind.Trader, "e-ticaret",
            "Sepete ekle. Ücretsiz kargo. 12 taksit imkanı. Hemen satın al."),
        Case(ProducerKind.Trader, "fason ettiren marka",
            "Ürünlerimizi fason olarak ürettiriyoruz, kendi markamızla satıyoruz."),

        // ---- Belirsiz: kural karar vermez, yapay zeka bakar (yanlis elemek, yanlis almaktan kotu)
        Case(ProducerKind.Unclear, "sadece sertifika",
            "Firmamız ISO 9001, CE ve TSE belgelidir, 40 ülkeye ihracat yapmaktadır."),
        Case(ProducerKind.Unclear, "zayif ticaret dili",
            "Yedek parça tedarik ediyoruz. Yetkili servis ağımızla hizmetinizdeyiz."),
        Case(ProducerKind.Unclear, "fabrika + yetkili bayi",
            "Kendi fabrikamızda ürettiğimiz ürünlerin yanında X markasının yetkili bayisiyiz."),
        Case(ProducerKind.Unclear, "guclu uretim + guclu ticaret",
            "35.000 m² kapalı alanda üretim yapıyoruz. Aynı zamanda Y firmasının yetkili bayisiyiz."),
        Case(ProducerKind.Unclear, "bos metin", ""),
        Case(ProducerKind.Unclear, "showroom m2 uretim degil",
            "Showroomumuz 300 m² alanda sizi bekliyor. Ürünlerimizi inceleyebilirsiniz."),
    };

    private static object[] Case(ProducerKind expected, string name, string text) => new object[] { expected, name, text };

    [Theory]
    [MemberData(nameof(Gold))]
    public void Gold_set_each_case_gets_the_expected_verdict(ProducerKind expected, string name, string text)
    {
        var e = ProducerSignals.Analyze(text);
        Assert.True(expected == e.Kind,
            $"{name}: beklenen {expected}, bulunan {e.Kind} (uretim {e.ProducerScore}, ticaret {e.TraderScore})");
    }

    [Fact]
    public void Gold_set_precision_and_recall_of_the_producer_verdict()
    {
        var rows = Gold.Select(o => (Expected: (ProducerKind)o[0], Found: ProducerSignals.Analyze((string)o[2]).Kind)).ToList();

        var predictedProducer = rows.Where(r => r.Found == ProducerKind.Producer).ToList();
        var actualProducer = rows.Where(r => r.Expected == ProducerKind.Producer).ToList();
        var precision = predictedProducer.Count == 0 ? 1.0 : predictedProducer.Count(r => r.Expected == ProducerKind.Producer) / (double)predictedProducer.Count;
        var recall = actualProducer.Count(r => r.Found == ProducerKind.Producer) / (double)actualProducer.Count;

        // Bayi kararinda yanlis pozitif (uretici sanip elemek) olmamali.
        var traderMistakes = rows.Count(r => r.Found == ProducerKind.Trader && r.Expected != ProducerKind.Trader);

        Assert.Equal(1.0, precision);
        Assert.True(recall >= 0.85, $"recall {recall:P0}");
        Assert.Equal(0, traderMistakes);
    }

    [Fact]
    public void Quote_is_the_sentence_from_the_site_and_numbers_do_not_split_it()
    {
        var e = ProducerSignals.Analyze("Hakkımızda. Firmamız 1987'den beri üretmektedir. 35.000 m² kapalı alanda kurulu fabrikamızda çalışıyoruz. İletişim: 0232 000 00 00");
        Assert.Equal(ProducerKind.Producer, e.Kind);
        Assert.Equal("35.000 m² kapalı alanda kurulu fabrikamızda çalışıyoruz.", e.BestQuote);
    }

    [Fact]
    public void Trader_quote_is_returned_for_a_dealer()
    {
        var e = ProducerSignals.Analyze("Biz Bosch'un Ege yetkili bayisiyiz. Servis ve satış.");
        Assert.Equal(ProducerKind.Trader, e.Kind);
        Assert.Contains("yetkili bayisiyiz", e.BestQuote);
    }

    [Fact]
    public void Repeated_menu_text_does_not_inflate_the_score()
    {
        var footer = string.Concat(Enumerable.Repeat("Sepete ekle | ", 30));
        var e = ProducerSignals.Analyze(footer);
        Assert.Equal(1, e.TraderScore);   // ayni kural bir kez sayilir
        Assert.Equal(ProducerKind.Unclear, e.Kind);
    }

    [Theory]
    [InlineData("Ege Beyaz Eşya | Arçelik Yetkili Bayisi", null, "yetkili bayi")]
    [InlineData("Bosch bulaşık makinesi", "İkinci el temiz, acil satılık", "ikinci el ilanı")]
    [InlineData("Bulaşık makinesi en ucuz fiyatlar", null, "fiyat karşılaştırma")]
    public void Retail_listing_hint_catches_dealer_and_classified_results(string title, string? snippet, string expected)
    {
        Assert.Equal(expected, ProducerSignals.RetailListingHint(title, snippet));
    }

    [Theory]
    [InlineData("Arçelik Bulaşık Makineleri Modelleri ve Fiyatları", "Yetkili satıcılar ve yetkili servis", null)]   // marka sahibi
    [InlineData("Bulaşık Makinesi Üreticisi", "Kiralık depo ve ikinci el makine de satıyoruz", null)]                // baslikta uretici
    [InlineData("Plastik Ambalaj Fabrikası", null, null)]
    [InlineData(null, null, null)]
    public void Retail_listing_hint_leaves_brand_owners_and_manufacturers_alone(string? title, string? snippet, string? expected)
    {
        Assert.Equal(expected, ProducerSignals.RetailListingHint(title, snippet));
    }

    // ---- Arama akisina baglanti

    [Fact]
    public void Evidence_brief_puts_the_site_quotes_in_front_of_the_ai()
    {
        var company = new Company { Name = "Ornek" };
        var proof = new Dictionary<Company, ProducerEvidence>
        {
            [company] = ProducerSignals.Analyze("35.000 m² kapalı alanda üretim yapıyoruz.")
        };
        var brief = IcpService.EvidenceBrief(proof, company);
        Assert.Contains("Sitede üretim kanıtı: «", brief);
        Assert.Contains("35.000 m²", brief);

        var empty = IcpService.EvidenceBrief(new Dictionary<Company, ProducerEvidence> { [company] = ProducerEvidence.None }, company);
        Assert.Contains("Sitede somut üretim kanıtı bulunamadı", empty);

        Assert.Equal(string.Empty, IcpService.EvidenceBrief(null, company));
        Assert.Equal(string.Empty, IcpService.EvidenceBrief(new Dictionary<Company, ProducerEvidence>(), company));
    }

    // ---- Adim 5: 4'lu sonuc (uretici / fason / bayi-yan sanayi / emin degil) ve zorunlu alinti

    private sealed class FakeAi : INaceClassifierAi
    {
        public Dictionary<int, ProductVerdict> Answer = new();
        public Task<Dictionary<int, string>> ClassifyNaceAsync(IReadOnlyList<(int Id, string Text)> c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Dictionary<int, ProductVerdict>> MatchProductsAsync(IReadOnlyList<(int Id, string Text)> c, IReadOnlyList<string> products, CancellationToken ct = default) => Task.FromResult(Answer);
    }

    private static readonly string[] Products = { "bulaşık makinesi", "çamaşır makinesi" };

    private static Company Firm(string name) => new() { Name = name, Industry = "Beyaz eşya" };

    [Theory]
    [InlineData("«35.000 m² kapalı alanda üretim yapıyoruz.»", true)]
    [InlineData("35.000 M² KAPALI ALANDA ÜRETİM YAPIYORUZ", true)]             // buyuk/kucuk harf ve tirnak farki
    [InlineData("35.000   m² kapalı alanda\nüretim yapıyoruz.…", true)]         // bosluk ve uc nokta farki
    [InlineData("Yılda 1 milyon adet bulaşık makinesi üretiyoruz.", false)]    // ozette yok: uydurma
    [InlineData("kısa", false)]
    [InlineData(null, false)]
    public void VerifyQuote_accepts_only_text_that_is_really_in_the_brief(string? evidence, bool expected)
    {
        var brief = "Ornek A.Ş. | Sitede üretim kanıtı: «35.000 m² kapalı alanda üretim yapıyoruz.»";
        Assert.Equal(expected, IcpService.VerifyQuote(evidence, brief) is not null);
    }

    [Fact]
    public async Task Producer_without_a_verifiable_quote_becomes_uncertain_not_producer()
    {
        var withQuote = Firm("Kanitli"); var invented = Firm("Uydurma"); var fason = Firm("Fason");
        var proof = new Dictionary<Company, ProducerEvidence>
        {
            [withQuote] = ProducerSignals.Analyze("35.000 m² kapalı alanda üretim yapıyoruz."),
            [invented] = ProducerEvidence.None,
            [fason] = ProducerSignals.Analyze("Başkaları adına fason üretim yapıyoruz. 20.000 m² kapalı alan.")
        };
        var ai = new FakeAi
        {
            Answer =
            {
                [1] = new(new() { "bulaşık makinesi" }, null, "uretici", "35.000 m² kapalı alanda üretim yapıyoruz."),
                [2] = new(new() { "bulaşık makinesi" }, null, "uretici", "Yılda 1 milyon adet bulaşık makinesi üretiyoruz."),   // ozette yok
                [3] = new(new() { "çamaşır makinesi" }, null, "fason", null)                                                   // alinti yok ama kural katmani kaniti var
            }
        };

        var r = await new IcpService(null!, null!, null!, ai).MatchProductsAsync(new[] { withQuote, invented, fason }, Products, default, proof);

        Assert.Equal(FirmKind.Producer, r.Kinds[withQuote]);
        Assert.Equal("35.000 m² kapalı alanda üretim yapıyoruz.", r.Quotes[withQuote]);

        Assert.Equal(FirmKind.Uncertain, r.Kinds[invented]);        // uydurma alinti kabul edilmez
        Assert.False(r.Quotes.ContainsKey(invented));
        Assert.Equal(new[] { "bulaşık makinesi" }, r.Matches[invented]);   // yine de listeden atilmaz

        Assert.Equal(FirmKind.Contract, r.Kinds[fason]);
        Assert.Contains("20.000 m²", r.Quotes[fason]);               // kural katmaninin cumlesi
        Assert.Empty(r.Reasons);
    }

    [Fact]
    public async Task Dealer_and_parts_maker_are_dropped_and_unsure_is_kept()
    {
        var dealer = Firm("Bayi"); var parts = Firm("Parca"); var unsure = Firm("Belirsiz"); var legacy = Firm("Eski bicim");
        var ai = new FakeAi
        {
            Answer =
            {
                [1] = new(new(), "yetkili bayi", "bayi"),
                [2] = new(new(), null, "yan_sanayi"),
                [3] = new(new(), "bilgi yetersiz", "emin_degil"),
                [4] = new(new() { "bulaşık makinesi" }, null)      // tur yok: eski davranis
            }
        };

        var r = await new IcpService(null!, null!, null!, ai).MatchProductsAsync(new[] { dealer, parts, unsure, legacy }, Products);

        Assert.Equal("yetkili bayi", r.Reasons[dealer]);
        Assert.Equal("yan sanayi (parça)", r.Reasons[parts]);
        Assert.False(r.Reasons.ContainsKey(unsure));
        Assert.Equal(FirmKind.Uncertain, r.Kinds[unsure]);
        Assert.False(r.Kinds.ContainsKey(legacy));
        Assert.Equal(new[] { "bulaşık makinesi" }, r.Matches[legacy]);
    }

    [Fact]
    public void ParseProductVerdicts_reads_kind_and_evidence()
    {
        var v = GeminiAiService.ParseProductVerdicts("""
            [{"id":1,"kind":"uretici","products":["bulaşık makinesi"],"evidence":" 35.000 m² kapalı alan "},
             {"id":2,"kind":"bayi","products":[],"reason":"bayi"}]
            """);
        Assert.Equal("uretici", v[1].Kind);
        Assert.Equal("35.000 m² kapalı alan", v[1].Evidence);
        Assert.Equal("bayi", v[2].Kind);
        Assert.Null(v[2].Evidence);
    }

    // ---- Adim 4: dizinlerden aday toplama (Google "site:" basliklarindan firma adi)

    [Theory]
    [InlineData("ARÇELİK A.Ş. - İstanbul - Bulaşık makineleri | Europages", "ARÇELİK A.Ş.")]
    [InlineData("Vestel Beyaz Eşya San. ve Tic. A.Ş. | OSTİM", "Vestel Beyaz Eşya San. ve Tic. A.Ş.")]
    [InlineData("Plastik Ambalaj Sanayi ve Ticaret A.Ş. - Gebze", "Plastik Ambalaj Sanayi ve Ticaret A.Ş.")]
    [InlineData("Girişim Makina Ltd. Şti. – Bursa", "Girişim Makina Ltd. Şti.")]
    [InlineData("Bulaşık makineleri - Türkiye tedarikçileri | Europages", null)]   // kategori sayfasi
    [InlineData("Tedarikçiler: bulaşık makinesi Türkiye", null)]                   // liste sayfasi
    [InlineData("OSB Firma Listesi | GOSB", null)]
    [InlineData("AB", null)]
    [InlineData(null, null)]
    public void Directory_titles_give_company_names_but_not_list_pages(string? title, string? expected)
    {
        Assert.Equal(expected, DirectoryCandidates.ExtractName(title, "bulaşık makinesi"));
    }

    [Fact]
    public void Directory_query_is_a_plain_site_search()
    {
        Assert.Equal("site:europages.com.tr bulaşık makinesi", DirectoryCandidates.Query("europages.com.tr", "bulaşık makinesi"));
        Assert.Contains("europages.com.tr", DirectoryCandidates.TurkishDirectories);
    }

    [Fact]
    public void Directories_are_never_candidates_themselves()
    {
        // Dizin sitelerinin kendisi firma gibi islenmesin: engel listesinde olmalilar.
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "EgebisLeadFinder", "appsettings.json"));
        Assert.Contains("\"europages\"", json);
        Assert.Contains("\"org.tr\"", json);   // ostim.org.tr, gosb.org.tr gibi
    }

    [Fact]
    public void Turkish_manufacturer_queries_use_production_wording_and_no_shopping_words()
    {
        var web = SearchQueryBuilder.Build(new SearchCriteria { Industry = "bulaşık makinesi", RegionKey = "TR", City = "İzmir", SapQueries = false });
        Assert.Contains(web, q => q.Contains("imalatı"));
        Assert.Contains(web, q => q.Contains("fason bulaşık makinesi üretimi"));
        Assert.Contains(web, q => q.Contains("manufacturer"));
        Assert.DoesNotContain(web, q => q.Contains("SAP"));
        Assert.All(web, q => Assert.DoesNotContain("fiyat", q, StringComparison.OrdinalIgnoreCase));
        Assert.All(web, q => Assert.DoesNotContain("satın al", q, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Places_search_without_city_is_split_into_industrial_cities_after_the_national_queries()
    {
        var queries = SearchQueryBuilder.BuildPlaceQueries(new SearchCriteria { Industry = "plastik ambalaj", RegionKey = "TR", Country = "Türkiye" });

        var firstCity = queries.FindIndex(q => q.Contains("Kocaeli"));
        var lastNational = queries.FindLastIndex(q => !SearchQueryBuilder.IndustrialCities.Any(c => q.Contains(c)));
        Assert.True(firstCity > 0);
        Assert.True(lastNational < firstCity, "sehre bolunmus sorgular ulusal sorgulardan sonra gelmeli");
        Assert.All(SearchQueryBuilder.IndustrialCities, city => Assert.Contains(queries, q => q.Contains(city)));
        Assert.All(queries, q => Assert.DoesNotContain("\"", q));
    }

    [Fact]
    public void Places_search_with_a_city_is_not_split()
    {
        var queries = SearchQueryBuilder.BuildPlaceQueries(new SearchCriteria { Industry = "plastik", RegionKey = "TR", City = "Bursa" });
        Assert.All(queries, q => Assert.Contains("Bursa", q));
        Assert.DoesNotContain(queries, q => q.Contains("Kocaeli"));
    }

    [Theory]
    [InlineData("Toptancı")]
    [InlineData("Wholesaler")]
    [InlineData("Distribütör")]
    [InlineData("İthalatçı")]
    public void Wholesale_and_import_categories_are_dropped_but_manufacturer_wins(string category)
    {
        Assert.False(PlaceCategoryFilter.IsRelevant(category));
        Assert.True(PlaceCategoryFilter.IsRelevant(category + " ve Üreticisi"));
    }

    [Fact]
    public void Marketplaces_and_b2b_directories_are_in_the_default_block_list()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "EgebisLeadFinder", "appsettings.json");
        if (!File.Exists(path)) return;   // testler baska bir klasorden calistirilirsa atla
        var json = File.ReadAllText(path);
        foreach (var domain in new[] { "akakce.com", "europages", "kompass.com", "alibaba.com", "ciceksepeti.com" })
            Assert.Contains($"\"{domain}\"", json);
    }
}
