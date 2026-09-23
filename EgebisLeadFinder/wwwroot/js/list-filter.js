// Detayli filtre cekmecesi (Firmalar / Lead'ler): bolge kartlari, il secim ekrani,
// secili il ozetleri. Tek dogru kaynak il kutucuklaridir (name="city"); bir bolgenin
// tum illeri seciliyse gonderimde "region=Ege" olarak kisaltilir.
(function () {
    'use strict';

    var form = document.querySelector('[data-list-filter]');
    if (!form) return;

    var drawer = form.closest('.filter-drawer');
    var pane = form.querySelector('[data-geo-pane]');
    var cityInputs = Array.prototype.slice.call(form.querySelectorAll('input[name="city"]'));
    var regionButtons = form.querySelectorAll('.geo-region');
    var regionAll = form.querySelectorAll('[data-region-all]');
    var chipsBox = form.querySelector('[data-geo-chips]');
    var summary = form.querySelector('[data-geo-summary]');
    var badge = form.querySelector('[data-geo-badge]');
    var search = form.querySelector('[data-geo-search]');

    function citiesOf(region) {
        return cityInputs.filter(function (i) { return i.dataset.region === region; });
    }

    function lower(s) { return (s || '').toLocaleLowerCase('tr'); }

    // Turkce karakterleri sadelestir: "izmir" -> "İzmir", "canakkale" -> "Çanakkale" bulunsun.
    function fold(s) {
        return lower(s).replace(/ç/g, 'c').replace(/ğ/g, 'g').replace(/ı/g, 'i').replace(/i̇/g, 'i')
            .replace(/ö/g, 'o').replace(/ş/g, 's').replace(/ü/g, 'u');
    }

    function sync() {
        var selected = cityInputs.filter(function (i) { return i.checked; });

        regionButtons.forEach(function (btn) {
            var list = citiesOf(btn.dataset.region);
            var n = list.filter(function (i) { return i.checked; }).length;
            btn.querySelector('[data-region-selected]').textContent = n;
            btn.classList.toggle('is-full', n === list.length);
            btn.classList.toggle('is-partial', n > 0 && n < list.length);
            btn.setAttribute('aria-pressed', n === list.length ? 'true' : n > 0 ? 'mixed' : 'false');
        });

        regionAll.forEach(function (box) {
            var list = citiesOf(box.dataset.regionAll);
            var n = list.filter(function (i) { return i.checked; }).length;
            box.checked = n === list.length;
            box.indeterminate = n > 0 && n < list.length;
            var group = box.closest('.geo-group');
            group.querySelector('[data-region-selected]').textContent = n;
            group.classList.toggle('has-selected', n > 0);
        });

        form.querySelectorAll('[data-geo-count]').forEach(function (el) { el.textContent = selected.length; });
        badge.hidden = selected.length === 0;
        badge.textContent = selected.length;
        summary.textContent = selected.length === 0 ? 'Tüm iller' : selected.length + ' il seçili';

        // Ozet cipleri: tam secili bolgeler tek cip, kalan iller tek tek.
        chipsBox.innerHTML = '';
        var covered = {};
        regionButtons.forEach(function (btn) {
            if (!btn.classList.contains('is-full')) return;
            citiesOf(btn.dataset.region).forEach(function (i) { covered[i.value] = true; });
            chipsBox.appendChild(chip(btn.dataset.region + ' bölgesi', function () { setRegion(btn.dataset.region, false); }));
        });
        selected.forEach(function (i) {
            if (covered[i.value]) return;
            chipsBox.appendChild(chip(i.value, function () { i.checked = false; sync(); }));
        });
    }

    function chip(text, onRemove) {
        var b = document.createElement('button');
        b.type = 'button';
        b.className = 'geo-chip';
        b.innerHTML = '<span></span><svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>';
        b.firstChild.textContent = text;
        b.setAttribute('aria-label', text + ' filtresini kaldır');
        b.addEventListener('click', onRemove);
        return b;
    }

    function setRegion(region, on) {
        citiesOf(region).forEach(function (i) { i.checked = on; });
        sync();
    }

    regionButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            setRegion(btn.dataset.region, !btn.classList.contains('is-full'));
        });
    });

    regionAll.forEach(function (box) {
        box.addEventListener('change', function () { setRegion(box.dataset.regionAll, box.checked); });
    });

    cityInputs.forEach(function (i) { i.addEventListener('change', sync); });

    // ---- il secim ekrani (cekmece icinde kayan sayfa) ----

    function openPane() {
        drawer.classList.add('show-cities');
        pane.setAttribute('aria-hidden', 'false');
        setTimeout(function () { search.focus(); }, 250);
    }

    function closePane() {
        drawer.classList.remove('show-cities');
        pane.setAttribute('aria-hidden', 'true');
        search.value = '';
        filterCities('');
    }

    form.querySelector('[data-geo-open]').addEventListener('click', openPane);
    form.querySelectorAll('[data-geo-close]').forEach(function (b) { b.addEventListener('click', closePane); });
    form.querySelector('[data-geo-clear]').addEventListener('click', function () {
        cityInputs.forEach(function (i) { i.checked = false; });
        sync();
    });

    drawer.addEventListener('hidden.bs.offcanvas', closePane);
    drawer.addEventListener('keydown', function (e) {
        // Il ekranindayken Esc once geri gelsin, cekmeceyi kapatmasin.
        if (e.key === 'Escape' && drawer.classList.contains('show-cities')) {
            e.preventDefault();
            e.stopPropagation();
            closePane();
        }
    }, true);

    function filterCities(q) {
        var term = fold(q.trim());
        var any = false;
        form.querySelectorAll('.geo-group').forEach(function (group) {
            var visible = 0;
            group.querySelectorAll('.geo-city').forEach(function (label) {
                var hit = !term || fold(label.dataset.cityName).indexOf(term) >= 0
                    || fold(group.dataset.regionGroup).indexOf(term) >= 0;
                label.hidden = !hit;
                if (hit) visible++;
            });
            group.hidden = visible === 0;
            if (visible) any = true;
        });
        form.querySelector('[data-geo-empty]').hidden = any;
    }

    search.addEventListener('input', function () { filterCities(search.value); });
    search.addEventListener('keydown', function (e) {
        // Enter formu gondermesin; tek sonuc varsa onu isaretlesin.
        if (e.key !== 'Enter') return;
        e.preventDefault();
        var visible = form.querySelectorAll('.geo-city:not([hidden])');
        if (visible.length === 1) {
            var input = visible[0].querySelector('input');
            input.checked = !input.checked;
            sync();
            search.select();
        }
    });

    // ---- gonderim: bos alanlari at, tam bolgeleri kisalt ----

    form.addEventListener('submit', function () {
        regionButtons.forEach(function (btn) {
            if (!btn.classList.contains('is-full')) return;
            citiesOf(btn.dataset.region).forEach(function (i) { i.disabled = true; });
            var hidden = document.createElement('input');
            hidden.type = 'hidden';
            hidden.name = 'region';
            hidden.value = btn.dataset.region;
            form.appendChild(hidden);
        });

        // URL temiz kalsin: varsayilan/bos degerler gonderilmesin.
        form.querySelectorAll('input, select').forEach(function (el) {
            if (el.disabled || !el.name) return;
            var empty = el.value === '' || (el.name === 'minScore' && el.value === '0');
            if ((el.type === 'radio' || el.type === 'checkbox') && !el.checked) return;
            if (empty) el.disabled = true;
        });
    });

    // Sayfa geri tusuyla donulurse devre disi alanlar geri gelsin.
    window.addEventListener('pageshow', function () {
        form.querySelectorAll('[disabled]').forEach(function (el) { el.disabled = false; });
        form.querySelectorAll('input[type="hidden"][name="region"]').forEach(function (el) { el.remove(); });
    });

    // "Siralama" butonuyla acilinca siralama bolumune odaklan.
    document.querySelectorAll('[data-focus-sort]').forEach(function (b) {
        b.addEventListener('click', function () {
            drawer.addEventListener('shown.bs.offcanvas', function once() {
                drawer.removeEventListener('shown.bs.offcanvas', once);
                var checked = form.querySelector('input[name="sort"]:checked');
                if (checked) checked.focus();
            });
        });
    });

    sync();
})();
