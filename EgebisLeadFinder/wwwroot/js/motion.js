/* Komuta Merkezi: kenar cubugu davranisi ve sayfa hareketleri.
   Yalnizca transform / opacity kullanilir; hareket azaltma tercihi saygi gorur. */
(function () {
    'use strict';

    var body = document.body;
    var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    function store(op, key, val) {
        try { return op === 'get' ? localStorage.getItem(key) : localStorage.setItem(key, val); } catch (e) { return null; }
    }
    function session(op, key, val) {
        try { return op === 'get' ? sessionStorage.getItem(key) : sessionStorage.setItem(key, val); } catch (e) { return null; }
    }

    // ---- Kenar cubugu: daralt / ac ----
    var collapseBtn = document.getElementById('egSideCollapse');
    var burger = document.getElementById('egBurger');
    var isDesktop = function () { return window.matchMedia('(min-width: 992px)').matches; };

    function setCollapsed(on) {
        body.classList.toggle('side-collapsed', on);
        store('set', 'eg-side', on ? 'collapsed' : 'open');
        var label = on ? 'Menüyü genişlet' : 'Menüyü daralt';
        if (collapseBtn) { collapseBtn.setAttribute('aria-label', label); collapseBtn.title = label; }
        if (burger && isDesktop()) { burger.setAttribute('aria-expanded', on ? 'false' : 'true'); burger.setAttribute('aria-label', label); }
        // Gosterge, genislik animasyonu bitince yeniden hizalanir.
        setTimeout(function () { if (window.egPlaceIndicator) window.egPlaceIndicator(); }, 260);
    }
    if (store('get', 'eg-side') === 'collapsed' && isDesktop()) {
        body.classList.add('side-collapsed');
        if (collapseBtn) { collapseBtn.setAttribute('aria-label', 'Menüyü genişlet'); collapseBtn.title = 'Menüyü genişlet'; }
    }
    // Ok hem daraltir hem (daraltilmisken) menuyu acik halde sabitler.
    if (collapseBtn) collapseBtn.addEventListener('click', function () { setCollapsed(!body.classList.contains('side-collapsed')); });

    // ---- Mobil cekmece ----
    var backdrop = document.getElementById('egSideBackdrop');
    function setDrawer(open) {
        body.classList.toggle('nav-open', open);
        if (burger) burger.setAttribute('aria-expanded', open ? 'true' : 'false');
        if (backdrop) backdrop.hidden = !open;
    }
    if (burger) burger.addEventListener('click', function () {
        if (isDesktop()) setCollapsed(!body.classList.contains('side-collapsed'));
        else setDrawer(!body.classList.contains('nav-open'));
    });
    // Ctrl+B: menuyu ac / kapat
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'b' && burger) {
            var t = e.target;
            if (t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))) return;
            e.preventDefault(); burger.click();
        }
    });
    if (backdrop) backdrop.addEventListener('click', function () { setDrawer(false); });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && body.classList.contains('nav-open')) setDrawer(false);
    });

    // ---- Aktif oge gostergesi: bir onceki sayfadaki yerinden yenisine kayar ----
    var nav = document.getElementById('egSideNav');
    var ind = nav && nav.querySelector('.eg-side-ind');
    function placeIndicator() {
        if (!nav || !ind) return;
        var active = nav.querySelector('.eg-side-link.is-active');
        if (!active) { ind.classList.remove('is-on'); return; }
        var y = active.offsetTop;
        var prev = parseFloat(session('get', 'eg-ind-y'));
        if (!reduce && isFinite(prev) && prev !== y) {
            ind.style.transition = 'none';
            ind.style.setProperty('--ind-y', prev + 'px');
            ind.classList.add('is-on');
            void ind.offsetWidth;   // baslangic konumu uygulansin
            ind.style.transition = '';
        }
        ind.style.setProperty('--ind-y', y + 'px');
        ind.classList.add('is-on');
        session('set', 'eg-ind-y', String(y));
    }
    window.egPlaceIndicator = function () { if (ind) ind.style.transition = ''; placeIndicator(); };
    placeIndicator();
    window.addEventListener('resize', function () { if (ind) { ind.style.transition = 'none'; placeIndicator(); } });

    // ---- Sayilar sayarak gelir (bir kez, 900 ms) ----
    function countUp(el) {
        var raw = el.textContent.trim();
        if (!/^\d{1,3}(\.\d{3})*$|^\d+$/.test(raw)) return;       // yalniz tam sayilar ("1.284", "62")
        var target = parseInt(raw.replace(/\./g, ''), 10);
        if (!isFinite(target) || target < 2 || target > 1e7) return;
        var start = performance.now(), dur = 900;
        function fmt(n) { return n.toLocaleString('tr-TR'); }
        function frame(now) {
            var t = Math.min(1, (now - start) / dur);
            var eased = 1 - Math.pow(1 - t, 3);
            el.textContent = fmt(Math.round(target * eased));
            if (t < 1) requestAnimationFrame(frame); else el.textContent = raw;
        }
        el.textContent = '0';
        requestAnimationFrame(frame);
    }
    if (!reduce) document.querySelectorAll('.stat-value').forEach(countUp);

    // ---- Puan halkalari: yuklemede bir kez dolar ----
    document.querySelectorAll('.eg-ring[data-p]').forEach(function (r) {
        var p = Math.max(0, Math.min(100, parseFloat(r.getAttribute('data-p')) || 0));
        if (reduce) { r.style.setProperty('--p', p); return; }
        var start = performance.now(), dur = 700;
        (function step(now) {
            var t = Math.min(1, (now - start) / dur);
            r.style.setProperty('--p', (p * (1 - Math.pow(1 - t, 3))).toFixed(1));
            if (t < 1) requestAnimationFrame(step);
        })(performance.now());
    });

    // ---- Acilir / kapanir kutular: [data-collapse] > [data-collapse-toggle] ----
    document.querySelectorAll('[data-collapse]').forEach(function (box) {
        var btn = box.querySelector('[data-collapse-toggle]');
        if (!btn) return;
        var key = box.getAttribute('data-collapse-key');
        var forced = box.getAttribute('data-collapse-force');
        var open = forced === 'open' ? true : (key && store('get', key) === 'open');
        function apply(v) { box.classList.toggle('is-open', v); btn.setAttribute('aria-expanded', v ? 'true' : 'false'); }
        apply(!!open);
        btn.addEventListener('click', function () {
            var v = !box.classList.contains('is-open');
            apply(v);
            if (key && forced !== 'open') store('set', key, v ? 'open' : 'closed');
        });
    });
})();
