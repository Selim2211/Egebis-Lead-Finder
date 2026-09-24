# Egebis Lead Finder

Potansiyel müşteri (lead) keşif aracı. Kullanıcı sektör ve şehir seçer; sistem
internetten üretici firmaları bulur, web sitelerini okur, yapay zeka ile
Egebis açısından uygunluklarını değerlendirir, iletişim kişilerini çıkarır ve
hazır şablondan kişiselleştirilmiş e-mail üretir.

**Hedef kitle:** SAP kullanan üretici firmalar ve fabrikalar.

## Mimari

Tek bir ASP.NET Core MVC uygulaması + PostgreSQL. Mikroservis, kuyruk, Redis,
vektör veritabanı yok.

```
ASP.NET Core MVC
       │
 ┌─────┼──────┬───────────┬──────────────┐
 ▼     ▼      ▼           ▼              ▼
Search Scraper AI    LeadScoring   EmailTemplate
 │      │      │          │              │
Serper HTTP  Gemini   (saf C#)       (saf C#)
 └──────┴──────┴──────────┴──────────────┘
                   │
              PostgreSQL
```

Yapay zeka yalnızca **firma analizinde** kullanılır. Puanlama ve e-mail üretimi
deterministik C# kodudur: daha ucuz, test edilebilir ve yanlış bilgi üretmez.

## Sunucu kurulumu (Docker)

Sunucuda yalnızca Docker (Compose eklentisiyle) gerekir. Uygulama **8091** portundan yayın yapar.

```bash
git clone https://github.com/Selim2211/Egebis-Lead-Finder.git
cd Egebis-Lead-Finder
docker compose up -d --build
```

Ardından `http://SUNUCU_IP:8091` adresini açın.

`.env` dosyası gerekmez; yoksa geliştirme varsayılanları kullanılır (veritabanı şifresi `egebis_dev`).
Canlıya çıkarken `cp .env.example .env` yapıp `POSTGRES_PASSWORD`'u değiştirin.

- PostgreSQL de compose içinde ayağa kalkar; veriler `pgdata` volume'unda kalıcıdır.
- Tablolar uygulama açılırken otomatik kurulur/güncellenir (elle migration gerekmez).
- API anahtarları (Serper, Gemini, Apollo), SMTP, IMAP ve Salesforce bilgileri uygulamadaki
  **Ayarlar** ekranından girilir ve veritabanında saklanır.
- Sağlık kontrolü: `http://SUNUCU_IP:8091/health`
- Güncelleme: `git pull && docker compose up -d --build`
- Loglar: `docker compose logs -f app`
- Salesforce bağlantısı için Ayarlar'daki "Uygulamanın dış adresi" alanına sunucunun
  HTTPS adresini yazın ve Salesforce'taki Callback URL'yi `https://ALAN_ADI/Salesforce/Callback` yapın.
- Mevcut yerel veriyi taşımak için: yerelde `pg_dump -U postgres --no-owner egebisleadfinder > yedek.sql`;
  sunucuda önce yalnız veritabanını başlatın (`docker compose up -d db`), yedeği yükleyin
  (`docker compose exec -T db psql -U egebis egebisleadfinder < yedek.sql`), sonra
  `docker compose up -d --build` ile uygulamayı başlatın.

## Kurulum (geliştirme)

### Gereksinimler
- .NET SDK 10
- PostgreSQL 16+

### Adımlar

1. Veritabanını oluşturun:

```bash
createdb -U postgres egebisleadfinder
```

2. API anahtarlarını User Secrets'a yazın (bunlar repoya girmez):

```bash
dotnet user-secrets set "Search:SerperApiKey" "<serper-anahtarı>" --project EgebisLeadFinder
```

```bash
dotnet user-secrets set "Ai:GeminiApiKey" "<gemini-anahtarı>" --project EgebisLeadFinder
```

```bash
dotnet user-secrets set "Search:Provider" "Serper" --project EgebisLeadFinder
```

Anahtarlar: [serper.dev](https://serper.dev) (2500 ücretsiz sorgu) ve
[Google AI Studio](https://aistudio.google.com/apikey).

`Search:Provider` değerini `Mock` bırakırsanız uygulama anahtarsız,
sahte verilerle çalışır.

3. Veritabanı şemasını oluşturun:

```bash
dotnet ef database update --project EgebisLeadFinder
```

4. Çalıştırın:

```bash
dotnet run --project EgebisLeadFinder
```

## Yapılandırma

Tüm ayarlar `appsettings.json` içinde:

| Bölüm | Ne işe yarar |
|---|---|
| `Search` | Arama sağlayıcısı, sonuç sayısı, kara liste (haber/ilan/rehber siteleri) |
| `Ai` | Gemini modeli, girdi karakter sınırı, zaman aşımı |
| `Scraper` | Okunacak sayfa yolları, sayfa sayısı, robots.txt, istekler arası bekleme |
| `Scoring` | Puan ağırlıkları ve ünvan puanları — kod değiştirmeden ayarlanır |
| `Smtp` | E-posta gönderimi (varsayılan kapalı) |
| `Pipeline` | Eş zamanlı işlenecek firma sayısı |

### Puanlama

Hedef kitle SAP kullanan üreticiler olduğu için en yüksek ağırlık SAP kanıtıdır:

```
SAP kullanımı doğrulandı    +35   (ihtimal varsa yarısı)
Üretici firma               +25
Hedef sektör                +15
IT/SAP yöneticisi bulundu   +10
E-posta bulundu             +10
Büyük ölçekli firma          +5
```

Şu firmalar **elenir** (0 puan): haber sitesi, iş ilanı platformu, bayi,
dernek/kamu kurumu ve **SAP hizmeti satan firmalar** (rakip).

## E-posta gönderimi

Varsayılan olarak kapalıdır; kullanıcı metni kopyalayıp kendi istemcisinden
gönderir. Açmak için `appsettings.json` içindeki `Smtp` bölümünü doldurup
`Enabled` değerini `true` yapın (parolayı User Secrets'a koyun).

Toplu gönderim yoktur: her e-posta ekranda görülüp tek tek gönderilir.
Kişisel verilerin işlenmesi ve ticari elektronik ileti gönderimi (KVKK, İYS)
yükümlülükleri gönderen tarafa aittir.

## Testler

```bash
dotnet test
```

## Geliştirme uçları

`Development` ortamında servisleri tek tek denemek için:

- `/dev/search?industry=Otomotiv&city=Bursa` — arama sorguları ve sonuçlar
- `/dev/scrape?url=https://firma.com` — site okuma çıktısı
- `/dev/analyze?url=https://firma.com` — okuma + yapay zeka analizi
- `/dev/pipeline?industry=Otomotiv&city=Bursa&max=5` — uçtan uca akış
