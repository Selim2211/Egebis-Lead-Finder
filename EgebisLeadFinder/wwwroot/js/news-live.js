/* Haber Akışı canlı ekran: ajan durumu, sayaçlar, geri sayım ve yeni kartların listeye kayarak eklenmesi. */
(function () {
    'use strict';
    var panel = document.getElementById('ntPanel');
    if (!panel) return;

    var liveUrl = panel.getAttribute('data-live-url');
    var cardsUrl = panel.getAttribute('data-cards-url');
    var canInject = panel.getAttribute('data-can-inject') === '1';
    var maxId = parseInt(panel.getAttribute('data-max-id'), 10) || 0;
    var compact = panel.getAttribute('data-compact') === '1';
    var grid = document.getElementById(panel.getAttribute('data-target') || 'ncGrid');
    var newBar = document.getElementById('nwNew');
    var newText = document.getElementById('nwNewText');
    var newBtn = document.getElementById('nwNewBtn');

    var elState = document.getElementById('ntState');
    var elQuery = document.getElementById('ntQuery');
    var elNext = document.getElementById('ntCountdown');
    var elBar = document.getElementById('ntBar');
    var nextTick = panel.getAttribute('data-next') || null;
    var restUntil = null;
    var timer = null;

    var STATES = {
        idle: { cls: '', label: 'CANLI', sub: 'Beklemede · sıradaki tur için hazır' },
        scanning: { cls: 'is-scanning', label: 'TARANIYOR', sub: '' },
        resting: { cls: 'is-resting', label: 'DİNLENİYOR', sub: 'Haber kaynağı geçici olarak yanıt vermedi; kısa süre sonra yeniden denenecek' },
        off: { cls: 'is-off', label: 'KAPALI', sub: 'Haber ajanı ayarlardan kapatılmış' }
    };

    function setCounter(id, value) {
        var el = document.getElementById(id);
        if (!el) return;
        var text = String(value);
        if (el.textContent === text) return;
        el.textContent = text;
        el.classList.remove('bump');
        void el.offsetWidth;
        el.classList.add('bump');
    }

    function applyAgent(a) {
        if (!a) return;
        var s = STATES[a.state] || STATES.idle;
        panel.classList.remove('is-scanning', 'is-resting', 'is-off');
        if (s.cls) panel.classList.add(s.cls);
        elState.textContent = s.label;

        if (a.state === 'scanning') {
            elQuery.innerHTML = '';
            var b = document.createElement('b');
            b.textContent = a.query || '…';
            elQuery.appendChild(document.createTextNode('Taranıyor: '));
            elQuery.appendChild(b);
            var c = document.createElement('span');
            c.className = 'nt-caret';
            elQuery.appendChild(c);
            elQuery.appendChild(document.createTextNode('  (' + (a.done || 0) + '/' + (a.total || 0) + ')'));
        } else if (a.state === 'resting' && a.restUntil) {
            var until = new Date(a.restUntil);
            elQuery.textContent = 'Dinleniyor · ' + until.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }) + "'e kadar";
        } else {
            elQuery.textContent = s.sub;
        }

        var pct = a.state === 'scanning' && a.total ? Math.round(100 * (a.done || 0) / a.total) : (a.state === 'idle' ? 100 : 0);
        elBar.style.width = pct + '%';
        nextTick = a.nextTick || nextTick;
        restUntil = a.restUntil || null;
    }

    function pad(n) { return n < 10 ? '0' + n : '' + n; }
    function tickCountdown() {
        if (!elNext) return;
        if (!nextTick) { elNext.textContent = '--:--'; return; }
        var ms = new Date(nextTick).getTime() - Date.now();
        if (ms <= 0) { elNext.textContent = 'şimdi'; return; }
        var s = Math.floor(ms / 1000);
        elNext.textContent = pad(Math.floor(s / 60)) + ':' + pad(s % 60);
    }
    setInterval(tickCountdown, 1000);
    tickCountdown();

    function inject(html) {
        if (!grid || !html || !html.trim()) return;
        var tpl = document.createElement('template');
        tpl.innerHTML = html.trim();
        var cards = Array.prototype.slice.call(tpl.content.children);
        cards.reverse().forEach(function (card) {
            card.classList.remove('is-new');
            card.classList.add('nc-enter');
            grid.insertBefore(card, grid.firstChild);
        });
        // Listeyi şişirmemek için sınırlı sayıda kart tutulur.
        var cap = compact ? 30 : 60;
        while (grid.children.length > cap) grid.removeChild(grid.lastChild);
        ['ncEmpty', 'nxLiveEmpty'].forEach(function (id) {
            var empty = document.getElementById(id);
            if (empty) empty.remove();
        });
    }

    function poll() {
        if (document.hidden) return schedule(20000);
        fetch(liveUrl + '?after=' + maxId, { headers: { 'Accept': 'application/json' }, cache: 'no-store' })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (d) {
                if (!d) return schedule(20000);
                applyAgent(d.agent);
                if (d.counters) {
                    setCounter('ntQueries', d.counters.queries);
                    setCounter('ntNew', d.counters.newToday);
                    setCounter('ntHot', d.counters.hotToday);
                    setCounter('ntWatched', d.counters.watched);
                }
                if (d.count > 0) {
                    if (canInject && grid) {
                        fetch(cardsUrl + '?after=' + maxId + (compact ? '&compact=true' : ''), { cache: 'no-store' })
                            .then(function (r) { return r.ok ? r.text() : ''; })
                            .then(function (html) { inject(html); maxId = d.maxId; })
                            .catch(function () { });
                        maxId = Math.max(maxId, 0);
                    } else if (newBar) {
                        newText.textContent = d.count + ' yeni haber' + (d.hot ? ' (' + d.hot + ' önemli)' : '') + ' — yenile';
                        newBar.classList.remove('d-none');
                    } else {
                        // Bu modülde kart listesi yok; yalnızca durum ve sayaçlar güncellenir.
                        maxId = Math.max(maxId, d.maxId || 0);
                    }
                } else {
                    maxId = Math.max(maxId, d.maxId || 0);
                }
                schedule(d.agent && d.agent.state === 'scanning' ? 4000 : 20000);
            })
            .catch(function () { schedule(30000); });
    }

    function schedule(ms) {
        clearTimeout(timer);
        timer = setTimeout(poll, ms);
    }

    if (newBtn) newBtn.addEventListener('click', function () { location.reload(); });
    document.addEventListener('visibilitychange', function () { if (!document.hidden) schedule(500); });
    schedule(4000);
})();
