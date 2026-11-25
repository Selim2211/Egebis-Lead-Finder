// Firmalar: "Toplu Excel'e aktar". Dugmeye basinca kartlarda secim kutusu cikar; secilen firmalar tek Excel
// dosyasinda (her firma ayri sayfada) indirilir. Secim ve mod sessionStorage'da tutulur: sayfa degisince korunur.
(function () {
    var toggle = document.getElementById('bulkExportToggle');
    var bar = document.getElementById('bulkBar');
    if (!toggle || !bar) return;

    var KEY = 'egebis.bulkExport';
    var max = parseInt(bar.getAttribute('data-max'), 10) || 100;
    var countEl = document.getElementById('bulkCount');
    var limitEl = document.getElementById('bulkLimit');
    var download = document.getElementById('bulkDownload');
    var boxes = Array.prototype.slice.call(document.querySelectorAll('.bulk-check-input'));

    function load() {
        try {
            var s = JSON.parse(sessionStorage.getItem(KEY) || 'null');
            if (s && Array.isArray(s.ids)) return { on: !!s.on, ids: s.ids.map(Number).filter(function (n) { return n > 0; }) };
        } catch (e) { /* bos */ }
        return { on: false, ids: [] };
    }
    var state = load();
    function save() {
        try { sessionStorage.setItem(KEY, JSON.stringify(state)); } catch (e) { /* gizli pencere vb. */ }
    }

    function render() {
        document.body.classList.toggle('bulk-mode', state.on);
        bar.hidden = !state.on;
        toggle.setAttribute('aria-pressed', state.on ? 'true' : 'false');
        toggle.classList.toggle('is-on', state.on);
        boxes.forEach(function (b) {
            var on = state.ids.indexOf(Number(b.value)) !== -1;
            b.checked = on;
            var card = b.closest('.company-card');
            if (card) card.classList.toggle('is-selected', state.on && on);
        });
        countEl.textContent = state.ids.length;
        limitEl.hidden = state.ids.length <= max;
        bar.classList.toggle('is-over', state.ids.length > max);
        download.disabled = state.ids.length === 0 || state.ids.length > max;
    }

    function setSelected(id, on) {
        var i = state.ids.indexOf(id);
        if (on && i === -1) state.ids.push(id);
        if (!on && i !== -1) state.ids.splice(i, 1);
    }

    toggle.addEventListener('click', function () {
        state.on = !state.on;
        if (!state.on) state.ids = [];
        save(); render();
    });

    boxes.forEach(function (b) {
        b.addEventListener('change', function () { setSelected(Number(b.value), b.checked); save(); render(); });
    });

    // Secim modunda kartin herhangi bir yerine tiklamak secer; kartin dugmeleri (ara, mail, sil...) bu modda devre disi.
    document.addEventListener('click', function (e) {
        if (!state.on) return;
        var card = e.target.closest && e.target.closest('.company-card');
        if (!card || e.target.closest('.bulk-check')) return;
        e.preventDefault();
        e.stopPropagation();
        var box = card.querySelector('.bulk-check-input');
        if (!box) return;
        setSelected(Number(box.value), !box.checked);
        save(); render();
    }, true);

    document.getElementById('bulkSelectPage').addEventListener('click', function () {
        var all = boxes.every(function (b) { return state.ids.indexOf(Number(b.value)) !== -1; });
        boxes.forEach(function (b) { setSelected(Number(b.value), !all); });
        save(); render();
    });
    document.getElementById('bulkClear').addEventListener('click', function () { state.ids = []; save(); render(); });
    document.getElementById('bulkCancel').addEventListener('click', function () { state = { on: false, ids: [] }; save(); render(); });

    download.addEventListener('click', function () {
        if (!state.ids.length || state.ids.length > max) return;
        var url = bar.getAttribute('data-export-url') + '?' + state.ids.map(function (id) { return 'ids=' + id; }).join('&');
        window.location.href = url;
        // Indirme baslar; secim modu kapanir.
        state = { on: false, ids: [] };
        save();
        setTimeout(render, 300);
    });

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && state.on && !document.querySelector('.modal.show')) { state = { on: false, ids: [] }; save(); render(); }
    });

    render();
})();
