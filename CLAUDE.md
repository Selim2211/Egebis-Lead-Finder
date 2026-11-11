# EgebisLeadFinder

ASP.NET Core 10 MVC + EF Core/PostgreSQL. Repo: Selim2211/Egebis-Lead-Finder (branch main). Kullanıcı Türkçe yazar; kısa, sade, teknik olmayan Türkçe cevap ver.

## Kurallar
- `git push` yalnızca kullanıcı "pushla" derse.
- Şifre/API anahtarı/token hiçbir alana girilmez; şifreleme anahtarı commit'lenmez, yazdırılmaz.
- Docker volume'leri (pgdata, appkeys) silinmez, `docker compose down -v` önerilmez.
- Yeni Türkçe arayüz metni için `EgebisLeadFinder/Resources/i18n/*.en.json` içine İngilizce karşılık eklenir (anahtarlar dosyalar arası tekrar etmesin).
- TLS doğrulaması kapatılmaz.
- Commit sonu: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`
- Canlı denemede yıkıcı/dışa dönük işlemler (Tümünü sil, mail gönder, Salesforce, Apollo reveal, gereksiz kredi harcayan arama) yapılmaz.

## Çalıştırma
- "localde başlat" = preview config `egebis` (port 5087).
- build/test/ef öncesi preview sunucusunu durdur (build çıktısını kilitler). Migration eklediysen `ef database update --no-build` öncesi yeniden derle.
- Testler: `dotnet test` (692 test geçmeli).

## Mimari notlar
- Arama Profili (SearchProfile) merkezli akış: arama için profil zorunlu; kullanıcı başına varsayılan profil (`AppUser.DefaultSearchProfileId`).
- `UsageScope` arama başına Serper/AI çağrılarını sayar → `SearchRun.SerperCredits/AiCalls`; günlük toplamlar `ApiUsageDaily/GeminiTokenDaily`; "Kullanılan Krediler" sayfası `CreditsReport` (Türkiye saati UTC+3).
- Npgsql `timestamptz` için DateTime.Kind=Utc olmalı (Unspecified/Local 500 verir).
- Black List: `SearchBlacklist` + `DomainHelper.IsBlocked`.
- Statik dosya muafiyeti `StaticPaths.IsStatic` (yalnızca gerçek statik klasörler).
- NACE aramasında ürün sınırı yok (bilinçli karar).

## Açık işler
- Kullanıcıya bağlı firma erişimi (IDOR) kararı bekliyor; SSRF, HTTPS/ters vekil, kurulum ekranı, giriş denemesi sınırı ayrı iş.
- Apollo anahtarı geçersiz; Groq ücretsiz kotası 429 verebilir; Gemini ücretli sürüme geçiş bekliyor.
