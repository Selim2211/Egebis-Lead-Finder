namespace EgebisLeadFinder.Configuration;

public class SearchOptions
{
    public const string Section = "Search";
    /// <summary>"Serper" veya "Mock".</summary>
    public string Provider { get; set; } = "Mock";
    public string SerperApiKey { get; set; } = string.Empty;
    public string SerperEndpoint { get; set; } = "https://google.serper.dev/search";
    public int ResultsPerQuery { get; set; } = 10;
    public List<string> BlockedDomains { get; set; } = new();

    /// <summary>
    /// Ayni Serper sorgusunun sonucu kac saat onbellekte tutulur. Tekrarlanan
    /// aramalarda kredi harcanmaz; 0 = onbellek kapali.
    /// </summary>
    public int CacheHours { get; set; } = 6;

    /// <summary>Bir takvim ayinda yapilabilecek en fazla Serper cagrisi; 0 = sinirsiz.</summary>
    public int MonthlyCreditCap { get; set; }

    /// <summary>
    /// Google Haritalar (Places) aramasi kullanilsin mi? Fabrikalarin SEO'su zayif
    /// oldugu icin organik arama az sonuc veriyor; Haritalar kaydi ise neredeyse
    /// hepsinde var. Kapatilirsa yalnizca organik arama calisir.
    /// </summary>
    public bool UsePlaces { get; set; } = true;

    public string SerperPlacesEndpoint { get; set; } = "https://google.serper.dev/places";

    /// <summary>
    /// Haritalar sorgusu basina cekilecek sayfa sayisi. Her sayfa 10 sonuc ve
    /// 1 Serper kredisi demektir.
    /// </summary>
    public int PlacesPagesPerQuery { get; set; } = 3;
}

public class AiOptions
{
    public const string Section = "Ai";
    public string Provider { get; set; } = "Gemini";
    public string GeminiApiKey { get; set; } = string.Empty;

    public string GeminiModel { get; set; } = "gemini-3.6-flash";
    public string GeminiEndpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models";
    public int MaxInputChars { get; set; } = 8000;
    public int TimeoutSeconds { get; set; } = 45;

    /// <summary>429/5xx tekrar denemelerinde ilk bekleme suresi (ms). Her denemede ikiye katlanir.</summary>
    public int RetryBaseDelayMs { get; set; } = 5000;

    // --- Maliyet tahmini: uygulama token sayisi tutmaz, bu yuzden Ayarlar
    // ekranindaki TL maliyeti asagidaki sabit fiyatlar + canli USD/TRY kuruyla
    // TAHMINI olarak hesaplanir (bkz. GeminiCostEstimator). Google fiyatlarini
    // degistirirse buradan guncellenir.

    /// <summary>Gemini Flash girdi fiyati, 1 milyon token basina USD.</summary>
    public decimal InputPricePerMillionTokensUsd { get; set; } = 0.10m;

    /// <summary>Gemini Flash cikti fiyati, 1 milyon token basina USD.</summary>
    public decimal OutputPricePerMillionTokensUsd { get; set; } = 0.40m;

    /// <summary>Ortalama girdi karakteri (tum site metni degil; MaxInputChars ust siniridir).</summary>
    public int EstimatedInputCharsPerCall { get; set; } = 4000;

    /// <summary>responseSchema JSON ciktisinin ortalama token sayisi tahmini.</summary>
    public int EstimatedOutputTokensPerCall { get; set; } = 400;
}

public class ScraperOptions
{
    public const string Section = "Scraper";
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxPagesPerSite { get; set; } = 6;
    public int MaxTextChars { get; set; } = 8000;
    public string UserAgent { get; set; } = "EgebisLeadFinder/1.0";
    public int DelayBetweenRequestsMs { get; set; } = 500;
    public bool RespectRobotsTxt { get; set; } = true;
    public List<string> CandidatePaths { get; set; } = new();
}

public class PipelineOptions
{
    public const string Section = "Pipeline";
    public int MaxParallelism { get; set; } = 4;
}

/// <summary>
/// Lead puanlama agirliklari. Dokumanin 10. bolumundeki tablo appsettings'ten ayarlanabilir.
/// </summary>
public class ScoringOptions
{
    public const string Section = "Scoring";
    public int Manufacturer { get; set; } = 30;
    public int TargetIndustry { get; set; } = 20;
    public int SapFound { get; set; } = 20;
    public int LargeCompany { get; set; } = 10;
    public int ItManagerFound { get; set; } = 10;
    public int EmailFound { get; set; } = 10;
    public List<string> TargetIndustryKeywords { get; set; } = new();
    /// <summary>Unvan parcasi -> puan. Kisi seciminde kullanilir (AI #2 yerine).</summary>
    public Dictionary<string, int> TitleScores { get; set; } = new();
}

public class SmtpOptions
{
    public const string Section = "Smtp";
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromName { get; set; } = "Egebis Bilişim";
    public string FromAddress { get; set; } = string.Empty;
}

public class ApolloOptions
{
    public const string Section = "Apollo";
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://api.apollo.io/api/v1/people/match";

    /// <summary>
    /// Kisi arama ucu. Domain + unvan filtresiyle karar vericileri dogrudan bulur.
    /// Onemli: "mixed_people/search" (klasik) çoğu Basic anahtarda 403 döner;
    /// "mixed_people/api_search" Basic dahil tüm planlarda erisilebilir ve kredi
    /// harcamaz — ama isim/e-posta gizli döner (id + unvan + has_email bayrağı).
    /// Tam veri icin sonradan Endpoint'e (people/match) id ile ikinci istek atilir.
    /// </summary>
    public string SearchEndpoint { get; set; } = "https://api.apollo.io/api/v1/mixed_people/api_search";

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Apollo'nun kendi sinifladigi kidem seviyeleri. Unvan metninden bagimsizdir:
    /// "Bilgi İşlem Müdürü" da "IT Manager" da Apollo tarafinda "manager" kidemine
    /// sinifladigi icin firma/dil jargonu farkindan etkilenmez. person_titles
    /// (Ayarlar'daki fonksiyon kelimeleri) ile BIRLIKTE kullanilir: ikisi de
    /// eslesmeli degil, biri eslesirse aday listeye girer (Apollo OR mantigi).
    /// </summary>
    public List<string> Seniorities { get; set; } = new()
    {
        "owner", "founder", "c_suite", "vp", "head", "director", "manager"
    };

    /// <summary>
    /// true ise Apollo, person_titles'daki kelimeleri tam eslesme yerine "ayni
    /// terimi iceren" unvanlara da genisletir (ör. "IT" -> "IT Coordinator").
    /// Apollo'nun kendi varsayilani da true; acikca gonderiyoruz.
    /// </summary>
    public bool IncludeSimilarTitles { get; set; } = true;
}

/// <summary>
/// LinkedIn arama + Apollo e-posta dogrulama akisinin tetiklenme kosullari.
/// Varsayilan olarak kapali: bu akis kisisel veri toplar, bilincli olarak acilmalidir.
/// </summary>
public class EnrichmentOptions
{
    public const string Section = "Enrichment";
    public bool Enabled { get; set; }
    public List<string> TriggerSapStatuses { get; set; } = new() { "yes", "likely" };
    public int MinCompanyScoreForEnrichment { get; set; } = 50;
    public int MaxCandidatesPerCompany { get; set; } = 3;
    public int MinTitleScore { get; set; } = 60;
}

/// <summary>
/// Faz-II firma on arastirma (rating) ayarlari. Anahtar kelimeler ve URL sablonlari
/// koda gomulu degil: skor agirliklari / kara liste gibi appsettings'ten ayarlanir.
/// </summary>
public class ResearchOptions
{
    public const string Section = "Research";

    /// <summary>Haber/itibar taramasinda calistirilacak azami sorgu sayisi.</summary>
    public int MaxNewsQueries { get; set; } = 6;

    /// <summary>Her sorgu icin Serper'dan istenecek organik sonuc sayisi.</summary>
    public int ResultsPerQuery { get; set; } = 8;

    /// <summary>Aday kaynak toplayicilarin es zamanli calisma sayisi.</summary>
    public int MaxParallelSources { get; set; } = 3;

    /// <summary>Bu gun sayisindan eski arastirmalar "guncel degil" sayilir.</summary>
    public int StalenessDays { get; set; } = 30;

    // --- Guven skoru agirliklari: clamp(src*w1 + snip*w2 + pozitif*w3, 0, 100) ---
    /// <summary>Veri donduren kaynak basina puan (en fazla 3 kaynak sayilir).</summary>
    public int ConfidenceSourceWeight { get; set; } = 15;

    /// <summary>Bilgi parcasi basina puan (en fazla 10 parca sayilir).</summary>
    public int ConfidenceSnippetWeight { get; set; } = 3;

    /// <summary>Pozitif kanit ekseni basina puan (en fazla 4 eksen sayilir).</summary>
    public int ConfidencePositiveWeight { get; set; } = 8;

    /// <summary>
    /// Risk isareti kelimeleri. Bir haber snippet'inde bunlardan biri gecerse
    /// deterministik degerlendirme "riskli" tarafina agirlik verir.
    /// </summary>
    public List<string> RiskKeywords { get; set; } = new()
    {
        "konkordato", "iflas", "haciz", "icra", "ödeme güçlüğü", "tasfiye",
        "batık", "kapandı", "kapanıyor", "işten çıkar", "dava açıldı", "el konuldu"
    };

    /// <summary>Yuksek onemli sayilan risk kelimeleri (tek basina "riskli" sinyali).</summary>
    public List<string> HighRiskKeywords { get; set; } = new()
    {
        "konkordato", "iflas", "haciz", "tasfiye", "el konuldu"
    };

    /// <summary>Buyume/pozitif isaret kelimeleri.</summary>
    public List<string> GrowthKeywords { get; set; } = new()
    {
        "yatırım", "yeni fabrika", "kapasite artış", "ihracat", "büyüme",
        "istihdam", "işe alım", "yeni tesis", "anlaşma imzala", "ihale kazan"
    };

    /// <summary>Finansal veri kelimeleri (ciro/bilanco haberlerini yakalamak icin).</summary>
    public List<string> FinanceKeywords { get; set; } = new()
    {
        "ciro", "bilanço", "gelir tablosu", "net kâr", "sermaye", "hasılat"
    };

    // --- Resmi kaynak "kendin aç" link sablonlari. {q} firma adiyla degistirilir. ---

    public string TicaretSicilUrlTemplate { get; set; } =
        "https://www.ticaretsicil.gov.tr/view/hizli_arama.php?unvan={q}";

    public string MersisUrl { get; set; } = "https://mersis.ticaret.gov.tr/";

    public string EkapSearchUrlTemplate { get; set; } =
        "https://ekap.kamuihale.gov.tr/EKAP/Ortak/IhaleArama/index.html";

    public string FindeksUrl { get; set; } = "https://www.findeks.com/ticari-rapor";

    /// <summary>
    /// KAP uye listesi JSON ucu. KAP sitesi Next.js SPA'ya gecti; bu ucun guncel
    /// adresi resmi olarak yayinlanmiyor. Bos birakilirsa dogrudan uye eslesmesi
    /// atlanir ve KAP kontrolu "site:kap.org.tr" aramasi uzerinden yapilir.
    /// Egebis tarayici gelistirici araclariyla guncel adresi bulup buraya yazarsa
    /// finansal rapor (hasilat/kar/ozkaynak) cekme yolu da aktiflesir.
    /// </summary>
    public string KapMemberListUrl { get; set; } = "";

    public string KapCompanyUrlTemplate { get; set; } =
        "https://www.kap.org.tr/tr/sirket-bilgileri/genel/{id}";

    /// <summary>KAP bildirim sorgu ucu (POST). Uye icin finansal raporlari listeler.</summary>
    public string KapDisclosureQueryUrl { get; set; } =
        "https://www.kap.org.tr/tr/api/memberDisclosureQuery";

    /// <summary>Tek bir bildirimin sayfasi. {index} disclosureIndex ile degistirilir.</summary>
    public string KapDisclosureUrlTemplate { get; set; } =
        "https://www.kap.org.tr/tr/Bildirim/{index}";

    /// <summary>Bir finansal raporun yapisal verisi (best-effort JSON). {index} disclosureIndex.</summary>
    public string KapFinancialReportUrlTemplate { get; set; } =
        "https://www.kap.org.tr/tr/api/financialReport/{index}";

    /// <summary>
    /// İSO 500 / Capital 500 / TİM 1000 gibi listelerde firma eslesmesi icin
    /// elle guncellenen referans dosyasi. ContentRoot'a gore cozulur.
    /// </summary>
    public string CompanyListDataPath { get; set; } = "Data/reference/company-lists.json";
}
