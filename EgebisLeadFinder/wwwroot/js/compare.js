// Firma karsilastirma sepeti: [data-compare-add] ile en fazla iki firma secilir,
// ekranin altinda "Karsilastir" cubugu cikar. Secim sekme boyunca (sessionStorage) korunur.
(function () {
    var KEY = 'eg.compare';

    function load() {
        try { return JSON.parse(sessionStorage.getItem(KEY) || '[]'); } catch (e) { return []; }
    }
    function save(list) {
        try { sessionStorage.setItem(KEY, JSON.stringify(list)); } catch (e) { /* gizli pencere */ }
    }

    var tray = document.createElement('div');
    tray.className = 'compare-tray no-print';
    tray.hidden = true;
    tray.setAttribute('role', 'region');
    tray.setAttribute('aria-label', 'Firma karşılaştırma');
    document.addEventListener('DOMContentLoaded', function () { document.body.appendChild(tray); render(); });

    function render() {
        var list = load();
        document.querySelectorAll('[data-compare-add]').forEach(function (b) {
            var on = list.some(function (x) { return String(x.id) === b.getAttribute('data-compare-add'); });
            b.classList.toggle('is-active', on);
            b.setAttribute('aria-pressed', on ? 'true' : 'false');
        });

        if (list.length === 0 || location.pathname.toLowerCase().indexOf('/company/compare') === 0) { tray.hidden = true; return; }
        tray.hidden = false;
        tray.innerHTML = '';

        var label = document.createElement('span');
        label.className = 'compare-tray-label';
        label.textContent = 'Karşılaştır';
        tray.appendChild(label);

        list.forEach(function (x) {
            var chip = document.createElement('span');
            chip.className = 'compare-tray-chip';
            chip.textContent = x.name;
            var remove = document.createElement('button');
            remove.type = 'button';
            remove.setAttribute('aria-label', x.name + ' seçimini kaldır');
            remove.textContent = '×';
            remove.addEventListener('click', function () { toggle(x.id, x.name); });
            chip.appendChild(remove);
            tray.appendChild(chip);
        });

        if (list.length < 2) {
            var hint = document.createElement('span');
            hint.className = 'compare-tray-hint';
            hint.textContent = 'İkinci firmayı seçin';
            tray.appendChild(hint);
        }

        var go = document.createElement('a');
        go.className = 'btn btn-sm btn-accent';
        go.textContent = 'Karşılaştır';
        go.href = '/Company/Compare?a=' + list[0].id + (list[1] ? '&b=' + list[1].id : '');
        if (list.length < 2) go.classList.add('disabled');
        tray.appendChild(go);

        var clear = document.createElement('button');
        clear.type = 'button';
        clear.className = 'btn btn-sm btn-link compare-tray-clear';
        clear.textContent = 'Temizle';
        clear.addEventListener('click', function () { save([]); render(); });
        tray.appendChild(clear);
    }

    function toggle(id, name) {
        var list = load();
        var i = list.findIndex(function (x) { return String(x.id) === String(id); });
        if (i >= 0) list.splice(i, 1);
        else {
            // Ucuncu secimde en eski secim cikar.
            if (list.length >= 2) list.shift();
            list.push({ id: Number(id), name: name });
        }
        save(list);
        render();
    }

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-compare-add]');
        if (!btn) return;
        e.preventDefault();
        toggle(btn.getAttribute('data-compare-add'), btn.getAttribute('data-company-name') || ('#' + btn.getAttribute('data-compare-add')));
    });

    window.egCompare = { clear: function () { save([]); render(); } };
})();
