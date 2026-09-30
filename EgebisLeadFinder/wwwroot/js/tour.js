// Sayfa turu: ekrandaki onemli alanlari sirayla vurgulayip aciklar (Kullanici Kilavuzu'nun
// "yonlendirmeli" kismi). Baslatma: ust menudeki "?" > "Bu sayfanin turu", adres cubugunda
// ?tour=1 veya ilk ziyarette cikan davet. Harici kutuphane yok.
(function () {
    // Her sayfanin adimlari. sel bulunamazsa adim atlanir; sel yoksa adim ekranin ortasinda cikar.
    var TOURS = [
        {
            key: 'panel', match: /^\/(home(\/index)?)?\/?$/i, title: 'Panel',
            steps: [
                { title: 'Panel', text: 'Satış hunisinin özeti: kaç firma bulundu, kaçıyla iletişime geçildi, kaç proje başladı. Kartlara tıklayınca ilgili listeye gidersiniz.' },
                { sel: '.stat-card', title: 'Sayı kartları', text: 'Toplam firma, lead, gönderilen mail gibi göstergeler. Kartın üzerine tıklayınca o kayıtların listesi açılır.' },
                { sel: '.funnel', title: 'Satış hunisi', text: 'Firmaların hangi aşamada olduğunu gösterir: temas yok → iletişim kuruldu → mail atıldı → proje başladı.' },
                { sel: '.nav-target', title: 'Biz ne arıyoruz?', text: 'Şirketinizin ne sattığını ve hedef segmentlerini buradan tanımlayın. Yapay zekâ bütün değerlendirmeleri bu tanıma göre yapar.' }
            ]
        },
        {
            key: 'search', match: /^\/company\/search/i, title: 'Firma Ara',
            steps: [
                { sel: '#basicIndustry', title: 'Sektör yazın', text: 'Aradığınız sektörü yazın (ör. "Otomotiv yan sanayi"). Sistem Google ve Google Haritalar\'da üretici firmaları bulur.' },
                { sel: '.segment-chips', title: 'Hedef segmentler', text: '"Biz ne arıyoruz?" ekranında tanımladığınız segmentler tek tıkla aramayı doldurur.' },
                { sel: '.smart-toggle', title: 'Akıllı arama', text: 'Açıkken yapay zekâ ek arama terimleri üretir ve bayi, rehber, rakip gibi hedef dışı adayları siteye girmeden eler.' },
                { sel: '.search-scope', title: 'Kapsam (bölge)', text: 'Aramanın yapılacağı ülke/bölge. Kalem simgesiyle değiştirin; Türkiye dışı bölgelerde sorgular o ülkenin dilinde kurulur.' },
                { sel: '[data-bs-target="#advancedSearchModal"]', title: 'Gelişmiş arama ve şablonlar', text: 'İl seçimi ve arama şablonu kaydetme burada. Şablonlar size özeldir; isterseniz tüm kullanıcılarla paylaşabilirsiniz (sonuçlarınız yine size özel kalır).' },
                { sel: '#companyNameInput', title: 'Belirli bir firmayı bul', text: 'Tanıdığınız bir firmayı adıyla bulup sisteme eklemek için.' }
            ]
        },
        {
            key: 'companies', match: /^\/company(\/index)?\/?$/i, title: 'Firmalar',
            steps: [
                { sel: '.run-picker', title: 'Hangi arama?', text: 'Varsayılan olarak son aramanızın sonuçları gösterilir. Buradan önceki aramalarınıza veya "Tüm firmalar"a geçebilirsiniz. Arama sonuçları kişiye özeldir.' },
                { sel: '.list-search', title: 'Liste içinde ara', text: 'Firma adı, sektör veya alan adıyla süzün.' },
                { sel: '.list-filter-btn', title: 'Detaylı filtre', text: 'Puan, sektör (NACE), il/ülke, arama şablonu, AI sinyali ve sıralama seçenekleri.' },
                { sel: '.stage-chips', title: 'Aşama ve segment çipleri', text: 'Satış aşamasına, ICP uyumuna, "Biz ne arıyoruz?" segmentine veya favorilerinize göre tek tıkla süzün.' },
                { sel: '.company-card', title: 'Firma kartı', text: 'Lead puanı, uygunluk yüzdesi, segment ve yapay zekânın kısa gerekçesi. Karta tıklayınca firma detayı açılır.' },
                { sel: '.company-card-fav', title: 'Favorilere ekle', text: 'Yıldız firmayı favori listenize ekler. Listeniz size özeldir; Favoriler ekranından herkese açabilirsiniz.' },
                { sel: '.company-card-actions', title: 'Hızlı işlemler', text: 'İletişim kuruldu / mail atıldı / proje başladı işaretleri, Salesforce\'a aktarma, karşılaştırmaya ekleme (Kıyasla) ve silme.' },
                { sel: '.export-menu, [data-export-menu], .page-head .dropdown', title: 'Dışa aktar', text: 'Ekranda gördüğünüz filtreli listeyi Excel veya CSV olarak indirin.' }
            ]
        },
        {
            key: 'details', match: /^\/company\/details/i, title: 'Firma detayı',
            steps: [
                { sel: '.score-xl', title: 'Lead puanı', text: 'Firmanın bizim için ne kadar iyi bir aday olduğunu gösterir (100 üzerinden). Puanın nasıl oluştuğu Özet sekmesinde yazar.' },
                { sel: '.detail-tabs', title: 'Sekmeler', text: 'Özet (puan dökümü, yapay zekâ analizi, konum), Firma analizi (internet ön araştırması) ve Kişiler & Lead\'ler.' },
                { sel: '.fav-star-labeled', title: 'Favori', text: 'Firmayı favori listenize ekleyin.' },
                { sel: '[data-compare-add]', title: 'Karşılaştır', text: 'Bu firmayı başka bir firmayla yan yana karşılaştırmak için seçin; ikinci firmayı seçince alttaki çubuktan karşılaştırmayı açın.' },
                { sel: 'a[href*="/Company/Report"]', title: 'Yazdırılabilir rapor', text: 'Görüşmeye götürmek için tek sayfalık firma raporu (PDF olarak kaydedilebilir).' },
                { sel: '#enrichButton', title: 'Karar vericileri bul', text: 'Apollo ve LinkedIn üzerinden hedef unvanlardaki kişileri arar. E-posta açmak ayrıca kredi harcar; hangi kişiye açılacağına siz karar verirsiniz.' }
            ]
        },
        {
            key: 'compare', match: /^\/company\/compare/i, title: 'Firma karşılaştırma',
            steps: [
                { sel: 'form.card', title: 'Firmaları seçin', text: 'Karşılaştırılacak iki firmayı seçin. Firmalar ekranındaki "Kıyasla" düğmesiyle de seçebilirsiniz.' },
                { sel: '.compare-head', title: 'Özet kartlar', text: 'Kurallara göre önde olan firma "Önde" rozetiyle işaretlenir.' },
                { sel: '.compare-table', title: 'Kriter tablosu', text: 'Kriterlerimize uygunluk, puan kalemleri, firma profili ve genel durum yan yana. Yeşil hücre o kriterde önde olan tarafı gösterir.' },
                { sel: 'form[action*="CompareAi"]', title: 'Yapay zekâ yorumu', text: 'Hangi firmaya önce gidilmeli, güçlü yanlar, riskler ve sonraki adımlar. Yorum rapora ve Excel\'e eklenir.' }
            ]
        },
        {
            key: 'favorites', match: /^\/favorites/i, title: 'Favoriler',
            steps: [
                { title: 'Favoriler', text: 'Yıldızladığınız firmalar burada toplanır. Liste varsayılan olarak size özeldir.' },
                { sel: '.fav-list-item', title: 'Listeler', text: 'Kendi listeniz ve diğer kullanıcıların herkese açtığı listeler. Başkasının listesini yalnızca görüntüleyebilirsiniz.' },
                { sel: 'form[action*="Visibility"]', title: 'Paylaşım', text: 'Listenizi tüm kullanıcılara açın veya tekrar kişiye özel yapın.' }
            ]
        },
        {
            key: 'sector', match: /^\/sector(\/index)?\/?$/i, title: 'Sektör analizi',
            steps: [
                { sel: 'input[name="q"]', title: 'Anahtar kelime', text: 'NACE kodu bilmenize gerek yok: sektörü günlük dille yazın (ör. "plastik ambalaj").' },
                { sel: '.nace-match', title: 'Eşleşen NACE kodları', text: 'Resmî listeden ve yapay zekâdan gelen kodlar; uygulamada o sektörde kaç firma olduğu da yazar. En fazla 6 kod seçin.' },
                { sel: '#analyzeForm button[type=submit]', title: 'Analiz et', text: 'Yapay zekâ her sektörün bize ne kadar uygun olduğunu puanlar; rapor kaydedilir, Excel ve PDF olarak alınabilir.' }
            ]
        },
        {
            key: 'profile', match: /^\/businessprofile/i, title: 'Biz ne arıyoruz?',
            steps: [
                { sel: '.bp-autofill', title: 'Sitemizden otomatik doldur', text: 'Şirket sitenizin adresini yazın; yapay zekâ ne sattığınızı, ideal müşterinizi ve hedef segmentleri taslak olarak çıkarır.' },
                { sel: '#profileForm', title: 'Şirket profili', text: 'Ne satıyoruz, ideal müşteri, müşterimiz olmayanlar ve rakipler. Arama, puanlama ve mail yazımı bu tanıma göre çalışır.' },
                { sel: '#segmentList', title: 'Hedef segmentler', text: 'Farklı ürün grupları için birden fazla hedef segment tanımlayın; Firma Ara ekranında tek tıkla aranır.' }
            ]
        },
        {
            key: 'icp', match: /^\/icp/i, title: 'İdeal Müşteri Profili',
            steps: [
                { sel: '.icp-suggest-card', title: 'Sitemizden ICP öner', text: 'Sitenizin adresini verin; yapay zekâ hedef sektörleri (NACE), bölge, ölçek ve hariç tutulacakları gerekçeleriyle önerir. "Forma ekle" ile uygulayıp kaydedin.' },
                { sel: '.icp-section', title: 'Hedef sektörler', text: 'Seçilen NACE bölümlerindeki firmalar "hedef sektör" puanı alır.' },
                { sel: 'input[name="exclude"]', title: 'Hariç tutulacaklar', text: 'Firma adı, sektör veya ürünlerde geçerse firma elenir (ör. bayi, distribütör).' },
                { sel: 'button[name="rescore"][value="true"]', title: 'Kaydet ve yeniden puanla', text: 'Kayıtlı firmaların puanları yeni ICP\'ye göre güncellenir; API kredisi harcamaz.' }
            ]
        },
        {
            key: 'leads', match: /^\/lead(\/index)?\/?$/i, title: 'Lead\'ler',
            steps: [
                { title: 'Lead\'ler', text: 'Firma detayında seçtiğiniz kişiler lead olur. Buradan durumlarını takip eder, mail yazar ve dizilere eklersiniz.' },
                { sel: '.list-search, input[name="q"]', title: 'Ara', text: 'Firma, kişi adı veya unvanla arayın. E-postalar şifreli saklandığı için e-postayla aramada adresin tamamını yazın.' },
                { sel: '.list-filter-btn', title: 'Filtre', text: 'Durum, e-posta var/yok, bölge ve sıralama.' }
            ]
        },
        {
            key: 'compose', help: 'leads', match: /^\/email\/compose/i, title: 'E-posta yazma',
            steps: [
                { sel: '#aiDraftButton', title: 'Yapay zekâ ile yaz', text: 'Firma analizine ve şirket profilimize göre kişiye özel ilk mail taslağı üretir.' },
                { sel: '#subject', title: 'Konu ve metin', text: 'Taslağı düzenleyin; taslak düzenleyicideki şablonları da kullanabilirsiniz.' },
                { sel: '#sendButton', title: 'Gönder', text: 'Ayarlar\'daki SMTP hesabından gönderilir; gönderilen her mail lead geçmişine kaydedilir.' }
            ]
        },
        {
            key: 'sequences', match: /^\/sequence/i, title: 'Mail dizileri',
            steps: [
                { title: 'Mail dizileri', text: 'Belirli aralıklarla otomatik takip mailleri. Kişi cevap verirse veya mail geri dönerse dizi durur.' },
                { sel: '.stat-card', title: 'Dizi durumu', text: 'Aktif, tamamlanan ve cevap alınan diziler.' }
            ]
        },
        {
            key: 'templates', help: 'sequences', match: /^\/template/i, title: 'Taslak düzenleyici',
            steps: [
                { sel: '#tplList', title: 'Taslaklar', text: 'Hazır e-posta şablonları. Soldan seçip düzenleyin.' },
                { sel: '#tplEditPane', title: 'Düzenle', text: 'Konu ve metin; {COMPANY_NAME}, {CONTACT_NAME} gibi alanlar gönderirken doldurulur.' },
                { sel: '#tplPreviewPane', title: 'Önizleme', text: 'Mailin alıcıda nasıl görüneceği.' }
            ]
        },
        {
            key: 'usage', match: /^\/usage/i, title: 'API kullanımı',
            steps: [
                { title: 'API kullanımı', text: 'Serper (arama), Gemini (yapay zekâ) ve Apollo (kişi) kullanımı, limitler ve kalan krediler. %80 ve %95\'te uyarı verir.' }
            ]
        },
        {
            key: 'settings', match: /^\/settings/i, title: 'Ayarlar',
            steps: [
                { title: 'Ayarlar (yönetici)', text: 'API anahtarları, arama limitleri, SMTP/IMAP ve Salesforce bağlantısı. Anahtarlar ve şifreler veritabanında şifreli saklanır.' },
                { sel: '#email-settings', title: 'E-posta', text: 'Mail gönderimi için SMTP hesabı; "Test e-postası gönder" ile deneyin.' },
                { sel: '#salesforce-settings', title: 'Salesforce', text: 'Firma ve kişileri Salesforce\'a aktarmak için bağlantı.' }
            ]
        },
        {
            key: 'users', match: /^\/users/i, title: 'Kullanıcılar',
            steps: [
                { title: 'Kullanıcılar (yönetici)', text: 'Kullanıcı ekleyin, rol verin (Yönetici / Kullanıcı), şifre sıfırlayın veya pasifleştirin. Her işlem Audit log\'a yazılır.' }
            ]
        },
        {
            key: 'audit', help: 'users', match: /^\/audit/i, title: 'Audit log',
            steps: [
                { title: 'Audit log (yönetici)', text: 'Kim, ne zaman, ne yaptı: girişler, aramalar, silmeler, ayar değişiklikleri. Tarih, kullanıcı ve işleme göre süzün.' },
                { sel: 'a[href*="format=xlsx"]', title: 'Excel\'e aktar', text: 'Filtredeki kayıtlar; kullanıcı/işlem özeti ve filtre bilgisi ayrı sayfalarda.' }
            ]
        }
    ];

    var path = location.pathname.replace(/\/+$/, '') || '/';
    var tour = TOURS.find(function (t) { return t.match.test(path); });
    window.egTour = { available: !!tour, start: function () { if (tour) run(tour); }, tours: TOURS };
    if (!tour) {
        document.addEventListener('DOMContentLoaded', function () {
            document.querySelectorAll('[data-tour-menu]').forEach(function (b) { b.closest('li').hidden = true; });
        });
        return;
    }

    function storageGet(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }
    function storageSet(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* gizli pencere */ } }

    var spot, pop, index = 0, steps = [];

    function visible(el) {
        if (!el) return false;
        var r = el.getBoundingClientRect();
        return r.width > 0 && r.height > 0;
    }

    function resolve(step) {
        if (!step.sel) return null;
        var list = document.querySelectorAll(step.sel);
        for (var i = 0; i < list.length; i++) if (visible(list[i])) return list[i];
        return null;
    }

    function run(t) {
        steps = t.steps.filter(function (s) { return !s.sel || resolve(s); });
        if (steps.length === 0) return;
        storageSet('eg.tour.seen.' + t.key, '1');
        closeOffer();
        index = 0;
        spot = document.createElement('div');
        spot.className = 'tour-spot';
        pop = document.createElement('div');
        pop.className = 'tour-pop';
        pop.setAttribute('role', 'dialog');
        pop.setAttribute('aria-live', 'polite');
        document.body.appendChild(spot);
        document.body.appendChild(pop);
        document.addEventListener('keydown', onKey);
        window.addEventListener('resize', place);
        window.addEventListener('scroll', place, true);
        show();
    }

    function end() {
        if (spot) spot.remove();
        if (pop) pop.remove();
        spot = pop = null;
        document.removeEventListener('keydown', onKey);
        window.removeEventListener('resize', place);
        window.removeEventListener('scroll', place, true);
    }

    function onKey(e) {
        if (e.key === 'Escape') end();
        else if (e.key === 'ArrowRight') go(1);
        else if (e.key === 'ArrowLeft') go(-1);
    }

    function go(delta) {
        var next = index + delta;
        if (next < 0) return;
        if (next >= steps.length) { end(); return; }
        index = next;
        show();
    }

    function show() {
        var s = steps[index];
        pop.innerHTML = '';

        var head = document.createElement('div');
        head.className = 'tour-pop-head';
        var count = document.createElement('span');
        count.className = 'tour-pop-count';
        count.textContent = (index + 1) + ' / ' + steps.length;
        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'tour-pop-close';
        close.setAttribute('aria-label', 'Turu kapat');
        close.innerHTML = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>';
        close.addEventListener('click', end);
        head.appendChild(count);
        head.appendChild(close);

        var title = document.createElement('div');
        title.className = 'tour-pop-title';
        title.textContent = s.title;
        var text = document.createElement('p');
        text.className = 'tour-pop-text';
        text.textContent = s.text;

        var nav = document.createElement('div');
        nav.className = 'tour-pop-nav';
        var guide = document.createElement('a');
        guide.href = '/Help#' + (tour.help || tour.key); // kilavuzdaki bolum
        guide.className = 'tour-pop-guide';
        guide.textContent = 'Kılavuzda oku';
        var back = document.createElement('button');
        back.type = 'button';
        back.className = 'btn btn-sm btn-outline-secondary';
        back.textContent = 'Geri';
        back.disabled = index === 0;
        back.addEventListener('click', function () { go(-1); });
        var next = document.createElement('button');
        next.type = 'button';
        next.className = 'btn btn-sm btn-accent';
        next.textContent = index === steps.length - 1 ? 'Bitir' : 'İleri';
        next.addEventListener('click', function () { go(1); });
        nav.appendChild(guide);
        nav.appendChild(back);
        nav.appendChild(next);

        pop.appendChild(head);
        pop.appendChild(title);
        pop.appendChild(text);
        pop.appendChild(nav);

        var el = resolve(s);
        if (el) el.scrollIntoView({ block: 'center', behavior: 'auto' });
        place();
        next.focus();
    }

    function place() {
        if (!pop) return;
        var el = resolve(steps[index]);
        var vw = document.documentElement.clientWidth, vh = window.innerHeight;
        if (!el) {
            spot.classList.add('is-center');
            spot.style.cssText = '';
            pop.style.left = Math.max(16, (vw - pop.offsetWidth) / 2) + 'px';
            pop.style.top = Math.max(16, (vh - pop.offsetHeight) / 2) + 'px';
            return;
        }
        spot.classList.remove('is-center');
        var r = el.getBoundingClientRect(), pad = 6;
        spot.style.left = (r.left - pad) + 'px';
        spot.style.top = (r.top - pad) + 'px';
        spot.style.width = (r.width + pad * 2) + 'px';
        spot.style.height = Math.min(r.height + pad * 2, vh - 20) + 'px';

        var pw = pop.offsetWidth, ph = pop.offsetHeight;
        var top = r.bottom + 14;
        if (top + ph > vh - 12) top = r.top - ph - 14;
        if (top < 12) top = Math.min(vh - ph - 12, Math.max(12, r.top + 12));
        var left = Math.min(Math.max(16, r.left), vw - pw - 16);
        pop.style.left = left + 'px';
        pop.style.top = top + 'px';
    }

    // ---- Ilk ziyarette davet ----
    var offer;
    function closeOffer() { if (offer) { offer.remove(); offer = null; } }

    function offerTour() {
        offer = document.createElement('div');
        offer.className = 'tour-offer no-print';
        offer.setAttribute('role', 'status');
        var text = document.createElement('div');
        text.innerHTML = '<div class="fw-semibold">Bu ekranı tanıyalım mı?</div><div class="small text-muted-2"></div>';
        text.querySelector('.small').textContent = tour.title + ' ekranının önemli bölümlerini adım adım gösterelim.';
        var actions = document.createElement('div');
        actions.className = 'd-flex gap-2 mt-2';
        var yes = document.createElement('button');
        yes.type = 'button';
        yes.className = 'btn btn-sm btn-accent';
        yes.textContent = 'Turu başlat';
        yes.addEventListener('click', function () { run(tour); });
        var no = document.createElement('button');
        no.type = 'button';
        no.className = 'btn btn-sm btn-outline-secondary';
        no.textContent = 'Şimdi değil';
        no.addEventListener('click', function () { storageSet('eg.tour.seen.' + tour.key, '1'); closeOffer(); });
        actions.appendChild(yes);
        actions.appendChild(no);
        offer.appendChild(text);
        offer.appendChild(actions);
        document.body.appendChild(offer);
    }

    document.addEventListener('DOMContentLoaded', function () {
        if (/[?&]tour=1\b/.test(location.search)) { setTimeout(function () { run(tour); }, 250); return; }
        // Turlar kapatildiysa (Ayarlar yerine tarayici bazli) davet cikmaz.
        if (storageGet('eg.tour.off') === '1' || storageGet('eg.tour.seen.' + tour.key) === '1') return;
        setTimeout(offerTour, 900);
    });

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-tour-start]');
        if (btn) { e.preventDefault(); run(tour); }
    });
})();
