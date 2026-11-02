# Lead Finder — Proje Bilgi Dokümanı (TÜBİTAK 2209-A başvurusu hazırlığı için)

> **Bu dokümanı okuyan yapay zekâ asistanına not:** Bu metin, projeyi geliştiren öğrencinin kod tabanını yazan
> asistan tarafından hazırlandı. Amaç: TÜBİTAK 2209-A (Üniversite Öğrencileri Araştırma Projeleri) başvuru
> formunu öğrenciyle birlikte hazırlarken projeye kod yazan kişi kadar hâkim olman. Aşağıdaki bilgiler kod
> tabanından ve yapılan testlerden alınmıştır; "doğrulanmadı" yazan yerler kesin değildir. Formu yazarken
> **Bölüm 12'deki başvuru kurallarına** mutlaka uy (marka/firma adı yasağı, Türkçe zorunluluğu vb.).

---

## 1. Kısa tanım

**Lead Finder**, işletmeden işletmeye (B2B) satış yapan firmaların **yeni müşteri adaylarını (lead) otomatik
bulmasını, yapay zekâ ile değerlendirmesini ve ilk iletişime kadar takip etmesini** sağlayan bir web
uygulamasıdır.

Kullanıcı "şu sektörde, şu bölgede müşteri arıyorum" der; sistem:

1. İnternette o sektör/bölgedeki firmaları arar,
2. Uygun olmayan adayları (haber sitesi, rehber, pazar yeri, bayi vb.) siteye girmeden eler,
3. Kalan firmaların web sitelerini okur,
4. Üretken yapay zekâ ile her firmayı **kullanıcı şirketin kendi tanımladığı ideal müşteri profiline** göre
   değerlendirir ve 0–100 arası **açıklanabilir** bir puan üretir,
5. Karar verici kişileri bulur,
6. Kişiye özel e-posta taslağı yazar (5 dile çevirebilir), gönderir ve otomatik takip e-postalarıyla süreci izler.

- **Proje kod adı:** LeadFinder
- **Mezuniyet projesi:** Yaşar Üniversitesi, MIS 4495 (Senior Project I) — MIS 4496 ile devam eder.
- **Geliştirme başlangıcı:** Eylül 2026 (ilk commit 23.09.2026). Ekim 2026 itibarıyla çalışan, test edilmiş bir prototip var.

---

## 2. Problem (neden yapıldı)

Küçük ve orta ölçekli B2B firmalar için yeni müşteri bulmak yavaş ve pahalıdır:

| Bugünkü durum | Sonuç |
|---|---|
| Müşteri adayları arama motorunda tek tek, elle aranıyor | Satış ekibinin zamanının büyük kısmı araştırmaya gidiyor |
| Bulunan firmanın gerçekten uygun olup olmadığı belirsiz | Uygun olmayan firmalara boşuna ulaşılıyor |
| Doğru karar vericiyi bulmak zor | İletişim yanlış kişiye gidiyor |
| Her firmaya ayrı e-posta yazmak vakit alıyor | Kişiselleştirme yapılmıyor ya da çok yavaş |
| Bilgiler tablolara, e-postalara, notlara dağılmış | Takip kaçıyor, kim ne yaptı belli değil |

Proje gerçek bir ihtiyaçtan doğdu: üretim yapan firmalara kurumsal yazılım (ERP) danışmanlığı veren bir
yazılım şirketinin, doğru üretici firmaları ve doğru kişileri bulma ihtiyacı. Sonra sistem **genelleştirildi**:
artık her şirket kendi ne sattığını ve ideal müşterisini tanımlayarak kendi işine göre kullanabiliyor
(örneğin testlerde boru/vana/tesisat malzemesi satan bir firmanın profiliyle çalıştırıldı).

> Başvuru formunda bu şirketin adı **yazılmamalıdır** (bkz. Bölüm 12). "Bir yazılım/danışmanlık şirketinin ihtiyacı"
> gibi genel ifade kullanılmalı.

---

## 3. Nasıl çalışır (uçtan uca akış)

### 3.1 Şirket profili ("Biz ne arıyoruz?")
Kullanıcı bir kez tanımlar:
- Ne sattığı, müşteri türü, ideal müşteri, **müşteri olmayanlar** (rakip, bayi vb.),
- Hedef segmentler (ör. "Mekanik Tesisat ve Taahhüt", "Endüstriyel Üretim ve Fabrikalar", "Altyapı ve İnşaat Projeleri"),
- İhtiyaç sinyalleri,
- **Puanlama anahtar kelimeleri** (ölçütler): `Ad | kelime, kelime | puan` biçiminde; yapay zekâ bu tanımdan
  otomatik önerir.

Şirketin web adresi yazılırsa yapay zekâ bu formu taslak olarak kendisi doldurur.

### 3.2 Arama
- Kullanıcı sektör, ülke/bölge ve firma sayısını (1–200) seçer.
- Türkiye'de il bazında; 15 Avrupa ülkesinde (Almanya, Avusturya, İsviçre, Hollanda, Belçika, Fransa, İtalya,
  İspanya, Birleşik Krallık, Polonya, Çekya, Romanya, Bulgaristan, Yunanistan, İsveç) eyalet/bölge bazında
  seçim yapılabilir; toplamda ~30 ülke/bölge desteklenir. Sorgular hedef ülkenin dilinde kurulur.
- **Akıllı arama:** Yapay zekâ sektöre ve şirket profiline göre ek arama terimleri üretir.
- Kaynak: genel web araması + harita (işletme) sonuçları.

### 3.3 Ön eleme (siteye girmeden)
Yapay zekâ, aday listesindeki başlık/adres/özetlere bakarak haber sitesi, rehber, pazar yeri, sosyal medya,
bayi gibi hedef dışı adayları eler. Ayrıca kural tabanlı bir engelli alan adı listesi vardır.
Örnek gerçek test: 22 aday bulundu → 12'si ön elemede elendi → 7'si analiz edildi → 3'ü zaten kayıtlıydı.

### 3.4 Site okuma
Firmanın web sitesi indirilir, düz metne çevrilir (en fazla ~8.000 karakter yapay zekâya gönderilir).
Okunamayan siteler "Elendi" değil dürüstçe **"İncelenemedi"** olarak işaretlenir.

### 3.5 Yapay zekâ analizi (yapılandırılmış çıktı)
Yapay zekâya site metni + şirket profili verilir; **JSON şemasına uygun** çıktı istenir:
firma adı, sektör, NACE kodu, üretici mi, ürünler, büyüklük, hedef segment, **uygunluk yüzdesi (fitScore)**,
uygunluk gerekçesi, eşleşen anahtar kelimeler, eleme nedeni (varsa).
Kural: "Metinde olmayan bilgiyi uydurma."

### 3.6 Puanlama (açıklanabilir, karma model)
Puan **yapay zekâ + deterministik kod** birleşimidir ve her kalemi "Puan Dökümü"nde gösterilir:

| Kalem | Hesap |
|---|---|
| Uygunluk (yapay zekâ) | `60 × fitScore / 100`; şirket anahtar kelime tanımladıysa × 0,7 |
| Anahtar kelime ölçütleri | Her eşleşen ölçüt kendi puanı (3–20), toplam en fazla 25 |
| Hedef sektör / bölge (ICP) | Yapılandırılabilir sabit puan |
| Büyük ölçekli firma | Sabit puan |
| Hedef unvanda kişi bulundu | Sabit puan |
| E-posta bulundu | Sabit puan |
| Toplam | En fazla 100; eleme nedeni varsa 0; doğrulanmamış firmalar en fazla 15 |

Örnek gerçek döküm (ekran görüntüsü: TUBITAK-Puan-Dokumu.png): "Uygunluk (yapay zekâ) %85 → +36,
Anahtar kelime: Mekanik Tesisat Taahhüt Hizmetleri → +20, Anahtar kelime: İnşaat ve Altyapı Müteahhitliği → +5,
Hedef unvanda kişi bulundu → +10, E-posta bulundu → +10 = 81". (85 × 60 / 100 = 51; ölçüt tanımlı olduğu için × 0,7 = 36.)
İş akışı şeması: TUBITAK-Is-Akisi-Semasi.png.

Bu yapı **akademik açıdan önemli**: puan kara kutu değil; her puanın nedeni görünür (açıklanabilir yapay zekâ).

### 3.7 Kişi bulma
Karar vericiler (genel müdür, satın alma, IT, üretim müdürü vb.) bir kişi/e-posta veri servisi ve sitedeki
iletişim bilgilerinden bulunur. Kredi harcayan işlemlerde kullanıcı önce uyarılır. E-postalar doğrulanır.

### 3.8 Derin firma araştırması (isteğe bağlı)
Haber araması, firma web sitesi, kamuya açık finansal bildirimler (borsa şirketleri için), sektör listeleri
(ör. büyük sanayi kuruluşları listeleri) ve resmi kayıt bağlantılarından derlenen bir rapor:
yönetici özeti, fırsatlar ve satış önerisi, ölçek, lokasyon, karar vericiler, IT altyapısı, riskler, kaynak listesi.
Bilgi yoksa "kaynaklarda bulunamadı" yazar, uydurmaz. Her iddia kaynak numarasıyla verilir.

### 3.9 İletişim ve takip
- Yapay zekâ kişiye özel e-posta taslağı yazar; tek tıkla **İngilizce, Almanca, Türkçe, Fransızca, İspanyolca**'ya çevrilir.
- SMTP ile gönderim; **mail dizileri** (otomatik takip e-postaları); IMAP ile **cevap algılama** (cevap veren kişiye dizi durur).
- Satış aşamaları: iletişim kuruldu / mail atıldı / cevap geldi / proje başladı (satış hunisi).
- Excel/CSV dışa aktarma, isteğe bağlı CRM (Salesforce) aktarımı.

### 3.10 Yönetim, güvenlik, maliyet
- **Roller:** Yönetici ve Kullanıcı; ayarlar ve kullanıcı yönetimi yalnız yöneticide.
- **Audit log:** Kim, ne zaman, ne yaptı (ayar değerleri değil yalnız anahtar adları yazılır).
- **Şifreleme:** Kişisel iletişim bilgileri ve API anahtarları veritabanında **AES-256-GCM** ile şifreli; giriş şifreleri PBKDF2 ile tek yönlü.
- **Kaba kuvvet koruması:** 5 hatalı girişte 15 dk kilit.
- **API kullanım ekranı:** Her servisin kullanımı, limiti, %80/%95 uyarıları; yapay zekâ maliyeti **gerçek token sayılarından** dolar ve TL olarak hesaplanır.
- **Çok dilli arayüz:** Türkçe/İngilizce; yapay zekâ çıktıları da seçili dilde üretilir.
- KVKK ve ticari e-posta konusunda kullanıcıya uyarı gösterilir.

---

## 4. Teknik mimari (özet)

- **Yapı:** Tek bir web uygulaması (monolit) + ilişkisel veritabanı. Mikroservis, kuyruk, vektör veritabanı yok
  (bilinçli sadelik).
- **Teknolojiler:** ASP.NET Core 10 MVC (C#), Entity Framework Core, PostgreSQL, Docker Compose ile kurulum.
- **Kod büyüklüğü:** ~25.000 satır C# (migration hariç), 19 controller, **471 otomatik birim testi** (xUnit, hepsi geçiyor), 44 commit.
- **Dış servisler (rolleriyle):**
  | Rol | Kullanım |
  |---|---|
  | Web/harita arama API'si | Firmaları bulmak |
  | Üretken yapay zekâ (büyük dil modeli) | Analiz, puanlama ölçütleri, ön eleme, e-posta, çeviri, araştırma raporu |
  | Kişi/e-posta veri servisi | Karar vericileri bulmak |
  | CRM (isteğe bağlı) | Kayıt aktarımı |
  | SMTP/IMAP | E-posta gönderme ve cevap algılama |
- **Yapay zekâ sağlayıcı soyutlaması:** Sistem tek bir sağlayıcıya bağlı değil. Ayarlardan seçilebiliyor:
  1. Ticari bir büyük dil modeli API'si (varsayılan, yapılandırılmış JSON şeması destekli),
  2. Ücretsiz deneme amaçlı bir model barındırma platformu (OpenAI uyumlu),
  3. Ücretsiz katmanlı hızlı çıkarım platformu (OpenAI uyumlu, şu an kullanılan).
  Tüm uygulama tek bir istek biçimi üretir; bir "adaptör" katmanı bunu seçilen sağlayıcının biçimine çevirir.
- **Model havuzu (yedekleme):** Aynı hesaptaki birden fazla model sıralı listede tutulur; biri dakikalık/günlük
  sınıra takılınca ya da kaldırılmışsa otomatik olarak sıradakine geçilir. Her analizde **hangi modelin**
  kullanıldığı kaydedilir ve gösterilir (karşılaştırılabilirlik için).
- **Ücretsiz katman uyumu:** Dakikalık token bütçesine sığmak için kaynak metin akıllıca kırpılır (talimat ve
  JSON şeması korunur), sınır aşımında sağlayıcının istediği süre kadar beklenir.

> Formda ticari ürün/servis **adları kullanılmamalı**: "büyük dil modeli tabanlı üretken yapay zekâ",
> "web arama API'si", "açık ağırlıklı büyük dil modelleri" gibi genel ifadeler kullan.

---

## 5. Bilimsel / araştırma yönü (formun en kritik kısmı)

2209-A bir **araştırma** programıdır; "bir yazılım yaptık" yeterli değildir. Projenin araştırma sorusu
şöyle kurulabilir:

**Ana araştırma sorusu (öneri):**
> Kamuya açık web verilerinden, üretken yapay zekâ ve açıklanabilir puanlama ile B2B potansiyel müşteri
> uygunluğu ne doğrulukta, ne tutarlılıkta ve ne maliyet/sürede belirlenebilir; bu süreç elle yapılan
> araştırmaya göre ne kadar verim sağlar?

**Alt sorular / hipotezler (öneri):**
1. **Doğruluk:** Yapay zekânın "uygun / uygun değil" kararı, alan uzmanının (satış uzmanı) kararıyla ne
   oranda örtüşür? (Ölçüm: doğruluk, kesinlik (precision), duyarlılık (recall), F1, Cohen's Kappa.)
2. **Tutarlılık:** Aynı firma aynı modelle tekrar analiz edildiğinde karar ve puan ne kadar değişir?
   (Ölçüm: tekrar testi; puan sapması, karar değişim oranı.)
3. **Model karşılaştırması:** Farklı büyük dil modelleri (ticari ve açık ağırlıklı) arasında doğruluk,
   tutarlılık, hız ve maliyet farkı nedir?
4. **Açıklanabilir puanlama:** Karma (yapay zekâ + kural tabanlı ölçüt) puanlama, yalnız yapay zekâ yüzdesine
   göre uzman sıralamasıyla daha iyi uyuşur mu? (Ölçüm: sıralama korelasyonu — Spearman.)
5. **Verimlilik:** Firma başına araştırma süresi ve maliyeti elle yönteme göre ne kadar azalır?
6. **Ön eleme etkisi:** Siteye girmeden yapılan yapay zekâ ön elemesi gereksiz analizleri ve maliyeti ne kadar
   azaltır, uygun firmaları ne oranda yanlışlıkla eler?

**Yöntem (öneri):**
- Bir sektör/bölge için örnek firma kümesi (ör. 100–200 firma) toplanır.
- Alan uzmanı (bağımsız, en az 1–2 kişi) her firmayı elle "uygun / uygun değil / kararsız" olarak etiketler
  → **altın standart (ground truth)** veri seti.
- Sistem aynı firmaları farklı modeller ve ayarlarla değerlendirir; sonuçlar etiketlerle karşılaştırılır.
- Tutarlılık için her firma 3 kez analiz edilir.
- Süre ve token/maliyet uygulamanın kendi kayıtlarından ölçülür (sistem zaten her çağrının token'ını ve
  süresini kaydediyor).
- Elle yöntemle süre karşılaştırması için kullanıcıların örnek firmaları elle araştırma süresi ölçülür.
- İstatistiksel analiz: karışıklık matrisi, F1, Kappa, Spearman, ortalama ± standart sapma.

**Özgün değer (öneri):**
- Şirkete özel, **kullanıcı tarafından tanımlanabilir** ideal müşteri profiliyle çalışan, sektörden bağımsız
  bir yaklaşım (genel amaçlı değil, her firmaya uyarlanır).
- **Açıklanabilir** puanlama: her puanın gerekçesi ve kalemleri görünür.
- Çok dilli ve çok ülkeli arama (Türkiye + Avrupa bölgeleri, hedef dilde sorgu).
- Farklı yapay zekâ sağlayıcı/modelleri arasında geçiş yapabilen ve karşılaştırma imkânı veren mimari.
- Uydurmayı (hallucination) azaltan tasarım: yapılandırılmış JSON şeması, "metinde yoksa uydurma" kuralı,
  kaynaklı araştırma raporu, okunamayan siteler için "İncelenemedi" ayrımı.

---

## 6. Ön bulgular (Ekim 2026'da yapılan gerçek testler)

Bunlar küçük örneklemli, **ön** sonuçlardır; formda "ön çalışma/ön deneme" olarak anılabilir, kesin sonuç
olarak sunulmamalıdır.

**a) Model erişilebilirliği ve hız (ücretsiz platformlarda, düşünme/akıl yürütme modu kapalı ve açık):**
- Denenen ~14 modelden 7'si hizmetten kaldırılmış (404/410) çıktı → ücretsiz katmanların **sürdürülebilirlik riski** gözlendi.
- Akıl yürüten (reasoning) yeni modeller, düşünme açıkken firma başına **45–128 sn**'de zaman aşımına uğradı.
- Düşünme kapatılınca bir model **6–19 sn**'ye indi ama **kalite çöktü**: uygun 5 firmanın 5'ini de yanlışlıkla
  eledi (görevi "firma bu ürünü satıyor mu?" diye ters yorumladı; model kartında Türkçe desteklenen diller arasında değildi).
- Hızlı çıkarım platformundaki 120 milyar parametreli açık ağırlıklı model (düşük akıl yürütme ayarıyla):
  6 firmanın 6'sını hatasız işledi, firma başına **8–37 sn** (site okuma dahil); bariz uygun ve bariz uygunsuz
  firmalarda doğru karar verdi.
- **Tutarlılık sorunu gözlendi:** Aynı firma (makine üreticisi bir fabrika) bir analizde %90 uygun, tekrarında
  "uygun değil" bulundu. Sınırdaki firmalarda karar değişkenliği var → araştırma sorusu 2'nin gerekçesi.
- Önceki ticari model ile yapılan analizlerde bu tür dalgalanma daha azdı (sistematik ölçülmedi).
- 15 firmalık gerçek arama testi: 22 aday → 12 ön elemede elendi → 7 analiz → 6 uygun (puan 63–71),
  2 elendi, 2 site okunamadı; ücretsiz katmanın dakikalık sınırına takılınca sistem bekleyip devam etti; toplam ~2–3 dk.

**b) Ücretsiz katman sınırları (kaynaklardan, resmi sayfadan doğrulanmadı):**
dakikada ~30 istek, ~8.000 token; günde ~1.000 istek ve ~200.000 token (model başına).
Bir firma analizi ~4–5 bin token → günde yaklaşık 40–50 firma analizi.

**c) Maliyet:** Ticari modelde maliyet gerçek token sayılarından hesaplanıyor; projenin ticari modeldeki
kredisi bittiği için şu an ücretsiz katman kullanılıyor (bu durum bütçe gerekçesi olarak kullanılabilir).

---

## 7. Proje çıktıları ve yaygın etki (öneri)

- Çalışan, açık mimarili bir prototip (web uygulaması, Docker ile kurulabilir).
- Etiketlenmiş bir değerlendirme veri seti (firma + uzman kararı) ve model karşılaştırma sonuçları.
- Sonuç raporu; mümkünse ulusal bir kongre bildirisi veya makale (yayın masrafı bütçeye yazılamaz).
- KOBİ'lerin ihracat ve yeni pazar bulma süreçlerine katkı (Avrupa bölgelerinde hedef dilde arama).
- Satış ekiplerinin araştırma süresinin azalması; daha az ama daha doğru hedeflenmiş iletişim (gereksiz e-posta azalır).
- Yerli yazılım ve yapay zekâ uygulaması geliştirme kapasitesi; öğrencinin yapay zekâ değerlendirme yöntemleri konusunda deneyimi.

---

## 8. İş paketleri ve zaman planı (öneri, 12 ay)

| İP | Ay | İş | Başarı ölçütü |
|---|---|---|---|
| 1 | 1–2 | Literatür taraması (LLM ile bilgi çıkarımı, lead scoring, açıklanabilir YZ), araştırma tasarımı | Literatür özeti, değerlendirme protokolü |
| 2 | 2–4 | Veri toplama ve uzman etiketleme (100–200 firma) | Etiketli veri seti, uzmanlar arası uyum (Kappa ≥ 0,6) |
| 3 | 3–6 | Model karşılaştırma deneyleri (en az 3 model), tutarlılık testleri | Doğruluk/F1/Kappa tabloları |
| 4 | 5–8 | Puanlama iyileştirme (karma puan, ölçüt ağırlıkları), tutarlılığı artırma (ör. çoklu oylama) | Uzman sıralamasıyla korelasyonda iyileşme |
| 5 | 7–10 | Saha denemesi (gerçek satış ekibiyle kullanım), süre/maliyet ölçümü | Elle yönteme göre süre azalması (%) |
| 6 | 10–12 | Sonuç raporu, bildiri/makale taslağı | Rapor teslimi |

**Riskler ve B planları:**
- Ücretsiz yapay zekâ modelinin kaldırılması/sınır değişmesi → sağlayıcı soyutlaması ve model havuzu zaten var; bütçeden ücretli kredi.
- Site okunamaması → "İncelenemedi" ayrımı; harita/özet verisiyle sınırlı değerlendirme.
- Uzman etiketleme süresi → örneklem 100 firmaya düşürülebilir; ikinci etiketleyici danışmandan destek.
- Kişisel veri riski → yalnız kamuya açık kurumsal veri, şifreleme, etik kurul muafiyeti/onayı başvurusu.

---

## 9. Bütçe (öneri, en fazla 12.000 TL)

İzin verilen kalemler: makine/teçhizat, sarf malzeme, seyahat, **hizmet alımı**.
İzin verilmeyenler: kongre/konferans katılımı, yayın, patent, konaklama, yeme-içme, harcırah.

Önerilebilecek kalemler (tutarlar örnek, güncel fiyatlarla netleştirilmeli):
- **Yapay zekâ API kullanım kredisi (hizmet alımı):** model karşılaştırma deneyleri için ticari model kredisi.
- **Web arama API kredisi (hizmet alımı):** veri toplama için arama sorguları.
- **Bulut sunucu kiralama (hizmet alımı):** prototipin saha denemesi için 12 ay sanal sunucu.
- Kişi/e-posta veri servisi kredisi (hizmet alımı), gerekiyorsa.

Faturalar proje yürütücüsü adına kesilmeli; harcamalar belgelenmeli. (Bu kalemlerin "hizmet alımı" olarak
kabulü TÜBİTAK değerlendirmesine bağlıdır.)

---

## 10. Etik ve hukuki konular

- Toplanan veriler **kamuya açık** kurumsal web sayfaları ve arama sonuçlarıdır.
- Kişi verileri (ad, unvan, kurumsal e-posta) KVKK kapsamında olabilir → şifreli saklama (AES-256-GCM),
  erişim rolleri, audit log mevcut; ticari elektronik ileti mevzuatına dair kullanıcı uyarısı gösteriliyor.
- Uzman etiketleme insan katılımcı içerir → **etik kurul onayı veya muafiyet belgesi** gerekebilir; danışmanla
  değerlendirilmeli.
- Yapay zekâ çıktıları yalnız karar destek amaçlıdır; nihai karar insandadır.
- **Üretken YZ beyanı:** Başvuru metninin hazırlanmasında üretken yapay zekâ kullanılıyorsa TÜBİTAK'ın
  "Destek Süreçlerinde Üretken Yapay Zekânın Sorumlu ve Güvenilir Kullanımı Rehberi"ne uygun **eksiksiz ve
  doğru beyan** edilmelidir; danışman içeriğin doğruluğundan sorumludur.

---

## 11. Bilinen sınırlılıklar (dürüstçe yazılmalı)

- Küçük modellerde karar tutarsızlığı (sınırdaki firmalar).
- Ücretsiz katmanda günlük kapasite sınırlı; büyük aramalar yavaş.
- Kaynak metin kırpıldığında derin araştırma daha az kapsamlı olabilir.
- Doğruluk henüz sistematik ölçülmedi (projenin araştırma kısmı bunu yapacak).
- Bazı siteler (JavaScript ağırlıklı / koruma altındaki) okunamıyor.

---

## 12. 2209-A BAŞVURU KURALLARI (formu yazarken mutlaka uy)

- **Son tarih:** Başvuru ve akademik danışman onayı **11 Kasım 2026, 17:30** (TYBS üzerinden). Kuruluş yetkilisi e-imza: 16–23 Kasım 2026.
- **Marka/firma adı yasağı:** Proje başlığı ve tüm metinlerde **hiçbir kurum, kuruluş, firma, ticari ürün veya
  marka öne çıkarılmamalı.** Yani:
  - Projenin doğduğu şirketin adı **yazılmaz**.
  - Kullanılan servislerin ticari adları **yazılmaz** → "büyük dil modeli tabanlı üretken yapay zekâ", "web arama API'si",
    "açık ağırlıklı büyük dil modeli", "hızlı çıkarım platformu", "CRM sistemi" gibi genel terimler kullanılır.
  - "Lead Finder" ürün adı olarak öne çıkarılmamalı; başlıkta yöntem/araştırma anlatılmalı.
- **Dil:** Öneri **Türkçe** olmalı; şablondaki **minimum karakter sınırlarına** uyulmalı (yoksa ön incelemede elenir).
- **Ayrımcı/dışlayıcı ifade** kullanılmamalı.
- **Değerlendirme ağırlıkları:** bilimsel nitelik %35, yöntem %25, proje yönetimi %20, yaygın etki %20.
- **Destek:** en fazla 12.000 TL, en fazla 12 ay. IBAN yürütücü öğrenciye ait olmalı.
- **Ekip:** yürütücü + en fazla 4 ortak; bir akademik danışman (YÖKSİS/ARBİS kaydı güncel).
- Aynı dönemde başka 2209-A/2209-B projesinde yer alınmamış olmalı; ARBİS kaydı zorunlu.
- Etik kurul gerekirse belge sonradan istenir.

**Önerilen başlık örnekleri (marka içermeyen):**
- "Kamuya Açık Web Verilerinden Üretken Yapay Zekâ ile Açıklanabilir B2B Potansiyel Müşteri Uygunluk Değerlendirmesi"
- "Büyük Dil Modelleri ile İşletmeler Arası Satışta Potansiyel Müşteri Tespiti: Doğruluk, Tutarlılık ve Maliyet Karşılaştırması"
- "KOBİ'ler İçin Açıklanabilir Yapay Zekâ Destekli Potansiyel Müşteri Keşif ve Puanlama Yöntemi"

---

## 13. Kullanılabilecek anahtar kavramlar (literatür için)

Lead scoring / potansiyel müşteri puanlama, B2B pazarlama otomasyonu, büyük dil modelleri ile bilgi çıkarımı
(information extraction), yapılandırılmış çıktı (structured output / JSON schema), açıklanabilir yapay zekâ (XAI),
LLM-as-a-judge ve değerlendirme güvenilirliği, LLM halüsinasyonu ve azaltımı, prompt mühendisliği,
web kazıma (web scraping) ve sınıflandırma, NACE sektör sınıflandırması, insan–YZ uyumu (Cohen's Kappa),
öz-tutarlılık (self-consistency), maliyet-performans dengesi.

---

## 14. Asistana çalışma önerisi

1. Önce öğrenciye danışman, ekip ve hangi sektör/bölgede deney yapılacağını sor.
2. Başlığı ve araştırma sorusunu Bölüm 5'e göre netleştir; marka adı kullanma.
3. TYBS'deki her alan için (özet, amaç, özgün değer, yöntem, iş planı, risk, yaygın etki, bütçe) Türkçe metin
   taslağı yaz; minimum karakter sınırlarını öğrenciye sor.
4. Ön bulguları (Bölüm 6) "ön çalışma" olarak kullan; abartma, kesin sonuç gibi sunma.
5. Bütçeyi izin verilen kalemlerle sınırla.
6. Üretken YZ kullanım beyanını hatırlat.
