using EgebisLeadFinder.Data;
using EgebisLeadFinder.Services;

namespace EgebisLeadFinder.Models;

/// <summary>Panel: huni adimi (firma → lead → mail → cevap → proje).</summary>
public record FunnelStep(string Label, int Count, string Url);

/// <summary>Panel: AI on arastirma sinyal dagilimi.</summary>
public record SignalSlice(string Label, int Count, string Key);

/// <summary>Panel: bir gunun aktivitesi (gonderilen mail / eklenen firma).</summary>
public record ActivityDay(DateTime Day, int Mails, int Companies);

/// <summary>Panel: son hareketler akisindaki tek olay.</summary>
public record RecentEvent(string Kind, string Title, string Subject, DateTime At, string Url);

/// <summary>Panel: sayilar, huni, aktivite ve yapilacaklar.</summary>
public class DashboardViewModel
{
    public int TotalCompanies { get; set; }
    public int PotentialLeads { get; set; }
    public int ToReview { get; set; }
    public int EmailReady { get; set; }
    public int Sent { get; set; }
    public List<Company> TopCompanies { get; set; } = new();

    // --- Bugün yapılacaklar ---
    public int FollowUpAfterDays { get; set; } = SettingKeys.DefaultFollowUpAfterDays;
    public int DueLeads { get; set; }
    public int LeadsWithoutEmail { get; set; }
    public int UnresearchedCompanies { get; set; }

    public List<FunnelStep> Funnel { get; set; } = new();
    public List<SignalSlice> Signals { get; set; } = new();
    public List<ActivityDay> Activity { get; set; } = new();
    public List<RecentEvent> Recent { get; set; } = new();

    /// <summary>Huni cubuklarinin genisligi icin en buyuk adim.</summary>
    public int FunnelMax => Funnel.Count == 0 ? 0 : Funnel.Max(f => f.Count);

    public int ActivityMax => Activity.Count == 0 ? 0 : Activity.Max(a => Math.Max(a.Mails, a.Companies));

    public int SignalTotal => Signals.Sum(s => s.Count);

    /// <summary>Panelde "yapacak bir sey yok" durumunu gostermek icin.</summary>
    public bool HasTodo => DueLeads > 0 || LeadsWithoutEmail > 0 || UnresearchedCompanies > 0 || ToReview > 0;
}

/// <summary>Firma Arama ekrani. Arama sonrasi sonuc ozeti de burada tasinir.</summary>
public class CompanySearchViewModel
{
    public const string ModeBasic = "basic";
    public const string ModeAdvanced = "advanced";
    public const string ModeAdvancedSave = "advanced-save";
    public const string ModeCompany = "company";

    /// <summary>Firma adiyla aramada islenecek en fazla aday site (ayni adli bayi/distributor vb.).</summary>
    public const int NameSearchMaxResults = 5;

    public SearchCriteria Criteria { get; set; } = new();
    public DiscoveryResult? Result { get; set; }
    public string? Error { get; set; }

    /// <summary>Hangi butonla gonderildi: temel arama, gelismis arama, gelismis + profil kaydet.</summary>
    public string? SubmitMode { get; set; }

    /// <summary>Gelismis arama ekranindaki kayitli profiller (en yeni en ustte).</summary>
    public List<SearchProfile> Profiles { get; set; } = new();

    /// <summary>Ayarlar'dan gelen azami firma sayisi (bilgi amacli gosterilir).</summary>
    public int MaxCompanies { get; set; } = SettingKeys.DefaultSearchMaxCompanies;

    public string DefaultCountry { get; set; } = SettingKeys.DefaultSearchCountry;

    /// <summary>Ayarlar'da secili arama bolgesi (ulke, dil ve il secimi bundan gelir).</summary>
    public SearchRegion Region { get; set; } = SearchRegions.Default;
}

/// <summary>Firma listesi ve filtreleri.</summary>
public class CompanyListViewModel
{
    public List<Company> Companies { get; set; } = new();
    public string? Search { get; set; }
    public int MinScore { get; set; }
    public bool OnlyWithoutLead { get; set; }

    /// <summary>Filtre acilir listesi icin tum arama profilleri.</summary>
    public List<SearchProfile> Profiles { get; set; } = new();

    /// <summary>Secili profil; null ise tum firmalar gosterilir.</summary>
    public int? ProfileId { get; set; }

    /// <summary>Satis asamasi filtresi (bkz. <see cref="CompanyStage"/>); null = hepsi.</summary>
    public string? Stage { get; set; }

    /// <summary>Asama cipleri icin toplam sayilar; "" anahtari = tum firmalar.</summary>
    public Dictionary<string, int> StageCounts { get; set; } = new();

    /// <summary>AI on arastirma sinyali filtresi: guclu | incelenmeli | riskli | none (arastirilmamis).</summary>
    public string? Signal { get; set; }

    public string Sort { get; set; } = CompanySort.Default;
    public GeoFilter Geo { get; set; } = new();
    public PagerModel Pager { get; set; } = new();

    /// <summary>Detayli filtre panelinde secili (siralama haric) filtre sayisi.</summary>
    public int ActiveFilterCount =>
        (string.IsNullOrWhiteSpace(Search) ? 0 : 1) + (MinScore > 0 ? 1 : 0) + (OnlyWithoutLead ? 1 : 0)
        + (ProfileId is > 0 ? 1 : 0) + (string.IsNullOrWhiteSpace(Signal) ? 0 : 1)
        + Geo.Countries.Count + Geo.Regions.Count + Geo.Cities.Count;
}

/// <summary>Lead'ler ekrani: kart listesi + arama/filtreler.</summary>
public class LeadListViewModel
{
    public List<Lead> Leads { get; set; } = new();
    public string? Query { get; set; }
    public LeadStatus? Status { get; set; }

    /// <summary>"yes" = iletisim kuruldu, "no" = kurulmadi, null = hepsi.</summary>
    public string? Contact { get; set; }

    /// <summary>"open" = e-postasi acik, "closed" = kapali, null = hepsi.</summary>
    public string? Email { get; set; }

    public int Total { get; set; }
    public int ContactedCount { get; set; }

    /// <summary>Lead Id → gonderilen e-posta sayisi.</summary>
    public Dictionary<int, int> EmailCounts { get; set; } = new();
    public Dictionary<LeadStatus, int> StatusCounts { get; set; } = new();

    public string Sort { get; set; } = LeadSort.Default;
    public GeoFilter Geo { get; set; } = new();
    public PagerModel Pager { get; set; } = new();

    /// <summary>Yalnizca takip bekleyen lead'ler gosteriliyor mu.</summary>
    public bool Due { get; set; }

    /// <summary>Takip bekleyen toplam lead (filtrelerden bagimsiz).</summary>
    public int DueCount { get; set; }

    /// <summary>Kac gun cevap gelmezse takip gerekiyor (Ayarlar).</summary>
    public int FollowUpAfterDays { get; set; } = SettingKeys.DefaultFollowUpAfterDays;

    /// <summary>Filtre sonucu toplam lead (sayfalamadan once).</summary>
    public int FilteredTotal => Pager.TotalItems;

    public bool HasFilter => !string.IsNullOrWhiteSpace(Query) || Status is not null
        || !string.IsNullOrWhiteSpace(Contact) || !string.IsNullOrWhiteSpace(Email) || Geo.Any || Due;

    /// <summary>Detayli filtre panelindeki (durum ciplerinden bagimsiz) filtre sayisi.</summary>
    public int ActiveFilterCount =>
        (string.IsNullOrWhiteSpace(Query) ? 0 : 1) + (string.IsNullOrWhiteSpace(Contact) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(Email) ? 0 : 1) + Geo.Countries.Count + Geo.Regions.Count + Geo.Cities.Count;
}

public class LeadDetailViewModel
{
    public Lead Lead { get; set; } = null!;

    /// <summary>Ayni firmadaki diger lead'ler (kisi bazli).</summary>
    public List<Lead> OtherLeads { get; set; } = new();

    /// <summary>Kac gun cevap gelmezse takip gerekiyor (Ayarlar).</summary>
    public int FollowUpAfterDays { get; set; } = SettingKeys.DefaultFollowUpAfterDays;
}

/// <summary>Firma satis takibi asamalari (ToggleStage ve Firmalar filtresi).</summary>
public static class CompanyStage
{
    public const string Contacted = "contacted";
    public const string Mailed = "mailed";
    public const string Project = "project";
    public const string Untouched = "untouched";
}

/// <summary>Firma detayi: analiz, kisiler ve varsa olusturulmus lead.</summary>
public class CompanyDetailViewModel
{
    public Company Company { get; set; } = null!;
    public CompanyAnalysis? Analysis { get; set; }
    public ScoreBreakdownViewModel? Breakdown { get; set; }
    /// <summary>Bu firmada acilmis tum lead'ler (kisi bazli), en yeni ustte.</summary>
    public List<Lead> Leads { get; set; } = new();
    public Contact? BestContact { get; set; }

    /// <summary>Faz-II on arastirma sonucu (Company.RatingJson'dan cozulur). Null ise hic arastirilmadi.</summary>
    public RatingViewModel? Rating { get; set; }
}

public class ScoreBreakdownViewModel
{
    public List<(string Reason, int Points)> Items { get; set; } = new();
    public string? DisqualifiedReason { get; set; }
    public bool Unverified { get; set; }
    public int Total { get; set; }
}

/// <summary>Detay ekranindaki "Firma Durumu - On Arastirma" karti icin.</summary>
public class RatingViewModel
{
    public CompanyRating Rating { get; set; } = null!;
    public RatingSignal Signal { get; set; } = RatingSignal.Bilinmiyor;
    public DateTime? RatedAt { get; set; }

    /// <summary>0-100 guven skoru. null = eski satir, hesaplanmamis.</summary>
    public int? Confidence { get; set; }

    /// <summary>Son arastirma <see cref="StalenessDays"/> gununden eski mi?</summary>
    public bool IsStale { get; set; }

    public int StalenessDays { get; set; } = 30;

    /// <summary>Yonlu gerekce satirlari (V2 varsa ondan, yoksa duz listeden map).</summary>
    public IReadOnlyList<EvaluatorNote> Notes { get; set; } = new List<EvaluatorNote>();

    /// <summary>Kaynak bazli ham parca gruplari (drill-down).</summary>
    public List<SourceSnippetGroup> SourceGroups { get; set; } = new();

    public string ConfidenceLabel => Confidence switch
    {
        null => "bilinmiyor",
        >= 67 => "yüksek",
        >= 34 => "orta",
        _ => "düşük"
    };

    public string ConfidenceCss => Confidence switch
    {
        null => "confidence-unknown",
        >= 67 => "confidence-high",
        >= 34 => "confidence-mid",
        _ => "confidence-low"
    };

    public string SignalLabel => Signal switch
    {
        RatingSignal.Guclu => "Güçlü",
        RatingSignal.Riskli => "Riskli",
        RatingSignal.Incelenmeli => "İncelenmeli",
        _ => "Bilinmiyor"
    };

    public string SignalCss => Signal switch
    {
        RatingSignal.Guclu => "signal-strong",
        RatingSignal.Riskli => "signal-risk",
        RatingSignal.Incelenmeli => "signal-review",
        _ => "signal-unknown"
    };
}

/// <summary>E-mail ekrani: sablon secimi ve doldurulmus onizleme.</summary>
public class EmailViewModel
{
    public Lead Lead { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public Contact? Contact { get; set; }
    public List<EmailTemplate> Templates { get; set; } = new();
    public int SelectedTemplateId { get; set; }
    public string? RecommendedTemplateKey { get; set; }
    public string? RecommendationReason { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ToAddress { get; set; }
    public bool SmtpEnabled { get; set; }
    public string? StatusMessage { get; set; }
    public bool IsError { get; set; }

    /// <summary>"Egebis Bilişim &lt;satis@egebis.com&gt;" — Ayarlar'daki gonderen adres.</summary>
    public string? SenderDisplay { get; set; }

    /// <summary>Hitapta kullanilan ad soyad; null ise "Sayın Yetkili" kullanildi.</summary>
    public string? SalutationName { get; set; }

    /// <summary>Bu kisiye daha once mail gitti; takip sablonu onerildi.</summary>
    public bool IsFollowUp { get; set; }

    /// <summary>Bu lead'e daha once atilan e-postalar.</summary>
    public List<SentEmail> History { get; set; } = new();

    /// <summary>Editorde gosterilen govde (temizlenmis HTML; gorseller metnin icinde).</summary>
    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>Kayitli gorseller (Data yuklenmez; resim /Email/Image/{id}'den gelir).</summary>
    public List<EmailImage> Images { get; set; } = new();
}

/// <summary>_LeadWait rozeti: lead + "kac gun sonra takip" esigi.</summary>
public record LeadWaitModel(Lead Lead, int AfterDays);

/// <summary>Paylasilan e-posta editoru (_MailEditor) icin veri.</summary>
public record MailEditorModel(
    string BodyHtml,
    string Body,
    List<EmailImage> Images,
    string Label = "E-mail",
    bool ShowPlaceholders = false);

/// <summary>Taslak Duzenleyici: soldaki liste + sagdaki editor.</summary>
public class TemplateEditorViewModel
{
    public List<EmailTemplate> Templates { get; set; } = new();

    /// <summary>Duzenlenen taslak; yeni taslakta Id = 0.</summary>
    public EmailTemplate Current { get; set; } = new();

    public bool IsNew => Current.Id == 0;

    /// <summary>Editorde gosterilen govde (yer tutucular doldurulmamis halde).</summary>
    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>Taslak basina gonderilen e-posta sayisi.</summary>
    public Dictionary<int, int> SentCounts { get; set; } = new();

    public List<EmailImage> Images { get; set; } = new();

    public string? Error { get; set; }
}

/// <summary>Ayarlar ekrani: API anahtarlari ve lead arama unvanlari.</summary>
public class SettingsViewModel
{
    /// <summary>Virgulle ayrilmis unvan anahtar kelimeleri.</summary>
    public string? LeadTitleKeywords { get; set; }

    public string? SerperApiKey { get; set; }
    public string? GeminiApiKey { get; set; }
    public string? ApolloApiKey { get; set; }

    // --- Firma arama parametreleri ---

    /// <summary>Arama basina getirilecek azami firma (skoru en yuksekten).</summary>
    public int SearchMaxCompanies { get; set; } = SettingKeys.DefaultSearchMaxCompanies;

    /// <summary>Temel aramada (il secilmeden) taranan ulke.</summary>
    public string? SearchDefaultCountry { get; set; } = SettingKeys.DefaultSearchCountry;

    /// <summary>Secili arama bolgesi anahtari (TR, DE, AE...).</summary>
    public string SearchRegion { get; set; } = SearchRegions.DefaultKey;

    /// <summary>Arama sonuclarindan elenecek ek alan adlari (virgulle ayrilmis).</summary>
    public string? ExtraBlockedDomains { get; set; }

    /// <summary>Mail atildiktan kac gun sonra lead takip bekliyor sayilir.</summary>
    public int FollowUpAfterDays { get; set; } = SettingKeys.DefaultFollowUpAfterDays;

    // --- E-posta gonderimi ---

    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromName { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string SmtpSecurity { get; set; } = "starttls";
    public string? SmtpUsername { get; set; }

    /// <summary>Formdan yeni sifre; bos gelirse kayitli sifre korunur (ekrana hic basilmaz).</summary>
    public string? SmtpPassword { get; set; }

    public bool SmtpPasswordSet { get; set; }
    public bool SmtpConfigured { get; set; }

    // Ayar bos ama User Secrets/appsettings'te deger varsa true.
    // Sistem calisir durumdadir, deger sadece bu ekrandan yonetilmiyordur.
    public bool SerperFromConfig { get; set; }
    public bool GeminiFromConfig { get; set; }
    public bool ApolloFromConfig { get; set; }

    // --- Gemini modeli ---
    /// <summary>Formdan gelen / kayitli secim; bos = varsayilan model.</summary>
    public string? GeminiModel { get; set; }

    /// <summary>Su an kullanilan model (secim yoksa appsettings varsayilani).</summary>
    public string GeminiCurrentModel { get; set; } = string.Empty;

    public string GeminiDefaultModel { get; set; } = string.Empty;
    public List<EgebisLeadFinder.Services.GeminiModelInfo> GeminiModels { get; set; } = new();
    public string? GeminiModelsError { get; set; }

    public string? StatusMessage { get; set; }

    // --- Serper kredi durumu ---

    /// <summary>Kullanicinin girdigi toplam kredi (ör. yeni anahtarda 2500).</summary>
    public int SerperCreditLimit { get; set; } = 2500;

    /// <summary>Son sifirlamadan bu yana yapilan Serper cagri sayisi.</summary>
    public int SerperCreditsUsed { get; set; }

    public DateTime? SerperUsageResetAt { get; set; }

    public int SerperCreditsRemaining => Math.Max(0, SerperCreditLimit - SerperCreditsUsed);

    /// <summary>0-100 arasi kullanim yuzdesi (bar genisligi icin).</summary>
    /// <summary>Bu takvim ayinda yapilan Serper cagrisi (onbellek isabetleri haric).</summary>
    public int SerperCallsThisMonth { get; set; }

    /// <summary>Aylik cagri tavani (appsettings Search:MonthlyCreditCap); 0 = sinirsiz.</summary>
    public int SerperMonthlyCap { get; set; }

    public int SerperUsagePercent => SerperCreditLimit <= 0
        ? 0
        : Math.Clamp((int)Math.Round(100.0 * SerperCreditsUsed / SerperCreditLimit), 0, 100);

    /// <summary>Bar rengi: %70 altı yeşil, %70-90 amber, üstü kırmızı.</summary>
    public string SerperUsageCss => SerperUsagePercent switch
    {
        >= 90 => "credit-bar-fill-danger",
        >= 70 => "credit-bar-fill-warn",
        _ => "credit-bar-fill-ok"
    };

    // --- Gemini kullanim durumu ---

    public int GeminiCallsToday { get; set; }
    public int GeminiCallsThisWeek { get; set; }
    public int GeminiCallsThisMonth { get; set; }

    /// <summary>Otomatik hesaplanan cagri basina tahmini maliyet (TL). Kullanici girmez.</summary>
    public decimal GeminiCostPerCallTry { get; set; }

    /// <summary>Maliyet hesabinda kullanilan USD/TRY kuru.</summary>
    public decimal UsdTryRate { get; set; }

    /// <summary>true: kur bu istekte canli cekildi. false: kur cekilemedi, son bilinen/varsayilan deger kullanildi.</summary>
    public bool UsdTryRateIsLive { get; set; }

    public decimal GeminiCostToday => Math.Round(GeminiCallsToday * GeminiCostPerCallTry, 2);
    public decimal GeminiCostThisWeek => Math.Round(GeminiCallsThisWeek * GeminiCostPerCallTry, 2);
    public decimal GeminiCostThisMonth => Math.Round(GeminiCallsThisMonth * GeminiCostPerCallTry, 2);
}
