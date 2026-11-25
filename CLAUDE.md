# EgebisLeadFinder

ASP.NET Core 10 MVC + EF Core/PostgreSQL. Repo: Selim2211/Egebis-Lead-Finder (branch main). Kullanıcı Türkçe yazar; kısa, sade, teknik olmayan Türkçe cevap ver.

## Kurallar
- `git push` yalnızca kullanıcı "pushla" derse. Pushtan önce `git log origin/main..HEAD` ile giden commit'lere bak (bir kez istenmeyen commit birlikte gitti).
- Şifre/API anahtarı/token hiçbir alana girilmez; şifreleme anahtarı (`App_Data/field-encryption.key`, gitignore'da) commit'lenmez, yazdırılmaz.
- Docker volume'leri (pgdata, appkeys) silinmez, `docker compose down -v` önerilmez.
- Yeni Türkçe arayüz metni için `EgebisLeadFinder/Resources/i18n/*.en.json` içine İngilizce karşılık eklenir (anahtarlar dosyalar arası tekrar etmesin).
- TLS doğrulaması kapatılmaz.
- Commit sonu: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`
- Commit'e araç klasörleri girmez: `.agents/ .claude-flow/ .claude/skills/ .swarm/ graphify-out/ reports/ research_notes/ skills-lock.json .graphifyignore`.
- Canlı denemede yıkıcı/dışa dönük işlemler (Tümünü sil, mail gönder, Salesforce, Apollo reveal, gereksiz kredi harcayan arama) yapılmaz.

## Çalıştırma
- "localde başlat" = preview config `egebis` (port 5087). Tarayıcı denemesi Claude in Chrome ile (kullanıcı orada giriş yapmış); açılışta "API servisleri" uyarı penceresi çıkar, kapat. Chrome sekmesi donarsa yeni sekme aç.
- build/test/ef öncesi preview sunucusunu durdur (build çıktısını kilitler).
- **Migration'lar yerelde kendiliğinden uygulanmaz** (`Database:MigrateOnStartup` yalnızca ayarlıysa): migration ekledikten sonra derle + `dotnet ef database update --no-build`. Sunucuda (Docker) `Database__MigrateOnStartup=true` yoksa elle uygulanmalı.
- Testler: `dotnet test` (899 test geçmeli).
- Ortamda python yok; betikler için `node -e` kullan. Dosyalar karışık CRLF/LF: çok satırlı değişiklikte Edit aracı daha güvenli.

## Mimari notlar
- Arama Profili (SearchProfile) merkezli akış: arama için profil zorunlu; kullanıcı başına varsayılan profil (`AppUser.DefaultSearchProfileId`).
- `UsageScope` arama başına Serper/AI çağrılarını sayar → `SearchRun.SerperCredits/AiCalls`; günlük toplamlar `ApiUsageDaily/GeminiTokenDaily`; "Kullanılan Krediler" sayfası `CreditsReport` (Türkiye saati UTC+3).
- Npgsql `timestamptz` için DateTime.Kind=Utc olmalı (Unspecified/Local 500 verir).
- Black List: `SearchBlacklist` + `DomainHelper.IsBlocked`; Firmalar listesi Black List'teki firmaları gizler (`CompanyController.EnsureBlacklistedIdsAsync`, bellekte eşleşme).
- Statik dosya muafiyeti `StaticPaths.IsStatic` (yalnızca gerçek statik klasörler).
- NACE aramasında ürün sınırı yok (bilinçli karar).
- **Tema:** en son yüklenen katman `wwwroot/css/theme-ice.css` (buz mavisi, koyu/açık). Renk değişikliği oraya; `theme-atelier.css` vb. eski katmanlara dokunma. Koyu modda beyaz kalan yer için tarama yöntemi: sayfayı aç, hesaplanan arka plan parlaklığına bak.
- **Yapay zekâ sağlayıcı** (`AiProviders`): Ayarlar'da Gemini/NVIDIA/Groq'tan biri. `AiProviders.Force(...)` (AsyncLocal) ile çağrı bazında zorlanır. Haber akışı varsayılan Groq (`Ai:NewsSource`, "main" = ana sağlayıcı). Gemini bozulursa açılış uyarısında yönetici "Groq'a geç" der (`Ai:GeminiFallback`="1"); `AiFallbackRecheck` her açılışta Gemini'yi yoklar, düzelince bayrağı kaldırır.
- **NACE:** `IcpService.ScoreAsync` → `NaceCatalog.ResolveCode` → `Refine` (4 haneli → 6 haneli, ürün kelimeleriyle) → `Verify` (kod sektör/ürünle uyuşmuyorsa resmi listeden en uygun 6 haneli; kök nadirliği ağırlıklı). `RefineNaceWithAiAsync` yalnız yeniden analizde (4 haneli kaldıysa aday listeyle AI'a sorar). Küçük model (gpt-oss-20b) yanlış sınıf seçebiliyor.
- **Şifreli alanlar** (AES-GCM, `FieldEncryption`, `enc:v1:`): kişi e-posta/telefon/kaynak/konum/başlık, lead notları, kullanıcı ad/e-posta, gelen mailler, bildirimler, gizli ayarlar. Kişi adı/unvanı bilinçli olarak açık (lead araması ve ada göre sıralama bunlara bağlı). `EncryptionBackfill` açılışta eski düz metni şifreler.
- **Excel'e aktar:** firma sayfasındaki yeşil düğme → `DataTransferController.Export` → `ExportService.CompanyDetailTables` (Firma/Kişiler/Leadler sayfaları; her satır başlık kadar hücre). İçe aktarma kullanıcı isteğiyle kaldırıldı. Firmalar listesinde de yeşil "Excel'e aktar" (`CompanyController.Export`, filtreli; CSV yanında küçük bağlantı): yönetici isteğiyle TEK SAYFA, her satır firma + bir kişisi (`ExportService.CompanyTables`; kişisiz firma tek satır, kişi sütunları boş). Favoriler aynı biçim. `ExportService.Clean` kontrol karakterlerini ve yarım emojiyi atar, hücreyi 32.000 karakterde keser (aşınca Excel dosyası oluşmuyordu); ondalık sayılar sayı olarak yazılır. Arama profili Excel'i (`SearchProfileTransfer`) 8 sütun: Ürünler ve çok ülke dahil kayıpsız geri yüklenir; içe aktarma sütunları başlık adıyla bulur (eski 7 sütunlu dosya da okunur), Excel'in sayıya çevirdiği NACE'yi düzeltir. Testler: `ExportDataMatrixTests`, `AllExcelTransfersTests`. **Toplu Excel'e aktar** (Firmalar): kartlarda seçim kutusu (`wwwroot/js/bulk-export.js`, seçim sessionStorage'da sayfalar arası korunur) → `DataTransferController.ExportMany` → `ExportService.ToXlsxCompanies`: ilk sayfa "Firmalar" listesi (bağlantılı), sonra her firma kendi sayfasında (tek firma Excel'iyle aynı bilgi, bölümler alt alta); en fazla 100 firma.
- **Ünvanlar:** `ContactTitle` kırpık ("…") ve firma adı olan ünvanı tanır, `Best` en tam olanı seçer. Apollo eşleşmesinin `title`ı (`PersonMatchResult.Title`) LinkedIn akışında ve "E-postayı Aç"ta kırpık/boş ünvanın yerine yazılır. `LinkedInTitleParser` "Ad - Firma A.Ş." başlığında firmayı ünvan saymaz. `PersonExtractor` site düğme metinlerini isim saymaz, cümleden yalnızca ünvanı alır. Ekranda ünvanlar kırpılmaz (theme-ice.css). Eski kayıtlarda 8 kırpık/firma adı ünvan var; Apollo açılınca düzelir.
- **Bildirimler:** panelde tek tek silme (×), "Tümünü sil" (iki tık onay), kaydırma (`NotificationService.DeleteAsync/DeleteAllAsync`).

## Haber akışı (Faz 2)
- `NewsScanService` 10 dk'da bir tur: takip edilen firmalar (takip + favori) ve radar konuları için Google Haberler RSS (yedek Serper); her turda ek akışlar `NewsFeedReader` (AA, Dünya, Ekonomim, Bloomberg HT RSS + Resmî Gazete ana sayfası) → `NewsFeedMatcher` ile firmaya/radar konusuna uyanlar alınır; tur başına 3 firma `GdeltNewsSource` (5 sn'de 1 istek sınırı, 429'da 30 dk ara; bu bağlantıdan sık 429 veriyor). KAP otomatik erişimi engelliyor (eklenemedi).
- Aynı olayın kopyası `NewsSimilarity` ile atlanır, `NewsDuplicateCleaner` her turda birleştirir. Olumsuz yatırım durdurma haberleri `NewsRuleClassifier.HaltRisk`.
- **TR / Global / Tümü:** `NewsFeedItem.Region` ("tr"/"global"/"" = bekliyor). Bölge haberin KONUSUNA göre (sitenin ülkesine değil): `NewsRegionRules.Decide` (firma ülkesi, radar şehri, Türk şehir/kurum adları → tr; yabancı ülke/şehir → global), belirsizse `INewsAi.ClassifyRegionsAsync` (Groq) turda karar verir. Seçim çerezde (`news-region`), `NewsController.InRegion` tüm bölümlere uygular (üstteki kayan şerit hariç).
- Şirketler sekmesi = yalnızca takip edilen firmalar (tüm firmalar değil); en çok haberi olan üstte.

## Açık işler
- Kullanıcıya bağlı firma erişimi (IDOR) kararı bekliyor; SSRF, HTTPS/ters vekil, kurulum ekranı, giriş denemesi sınırı ayrı iş.
- Apollo anahtarı geçersiz; Groq ücretsiz kotası 429 verebilir; Gemini ücretli sürüme geçiş bekliyor.
- Global haber az: yurt dışı kaynak yalnız GDELT (429). Öneri: Google Haberler İngilizce baskısı eklemek.
- MEKA GRUP gibi eski firmaların yanlış NACE'si yeniden analiz/yeniden puanlamada düzelir (kayıtlı kod kendiliğinden değişmez).
- "Yeniden analiz sonrası puan Firmalar'a yansımıyor" şikâyeti (yönetici) yerelde yeniden üretilemedi; Firmalar listesine `no-store` eklendi. Tekrar ederse önce/sonra puanları istenecek.
- Kullanım kılavuzu ve teknik doküman (Word) masaüstünde; haber akışı ve sonraki eklemeler bu belgelerde yok.
