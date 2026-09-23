// Uygulama geneli pop-up'lar: onay penceresi (egDialog) ve kose bildirimleri (egToast).
// Tarayicinin kendi confirm()/alert() kutulari yerine bunlar kullanilir.
//
// Bildirim ciktilari:
//   <div class="alert ..." data-flash="success|error|info|warning">Mesaj</div>
//     -> sayfa acilinca kose bildirimine donusur (JS yoksa bant olarak kalir).
// Onay istemek icin (form veya submit butonu uzerinde):
//   data-confirm="Mesaj" data-confirm-title="Baslik" data-confirm-ok="Evet, sil"
//   data-confirm-tone="danger|warning|credit|info" data-confirm-subject="Vurgulanan ad"
(function () {
    'use strict';

    var ICONS = {
        danger: '<path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6M10 11v6M14 11v6"/>',
        warning: '<path d="m21.7 18-8-14a2 2 0 0 0-3.4 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.7-3Z"/><path d="M12 9v4M12 17h.01"/>',
        credit: '<circle cx="12" cy="12" r="9"/><path d="M14.8 9A3 3 0 0 0 12 7.5c-1.7 0-3 1-3 2.3s1.3 1.9 3 2.2 3 .9 3 2.2-1.3 2.3-3 2.3a3 3 0 0 1-2.8-1.5M12 6v1.5M12 16.5V18"/>',
        info: '<circle cx="12" cy="12" r="9"/><path d="M12 16v-4M12 8h.01"/>',
        success: '<path d="M20 6 9 17l-5-5"/>',
        error: '<circle cx="12" cy="12" r="9"/><path d="m15 9-6 6M9 9l6 6"/>'
    };

    function svg(name, size) {
        return '<svg width="' + size + '" height="' + size + '" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
            + 'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + (ICONS[name] || ICONS.info) + '</svg>';
    }

    function el(tag, cls, html) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (html != null) e.innerHTML = html;
        return e;
    }

    // ---------------- Onay penceresi ----------------

    var dialog = (function () {
        var root, box, icon, title, msg, subject, okBtn, cancelBtn, resolver, lastFocus, isAlert;

        function build() {
            root = el('div', 'eg-dialog-root');
            root.hidden = true;
            root.innerHTML =
                '<div class="eg-dialog-backdrop" data-close></div>' +
                '<div class="eg-dialog" role="alertdialog" aria-modal="true" aria-labelledby="egDialogTitle" aria-describedby="egDialogMsg">' +
                '  <div class="eg-dialog-icon"></div>' +
                '  <h2 class="eg-dialog-title" id="egDialogTitle"></h2>' +
                '  <div class="eg-dialog-subject"></div>' +
                '  <p class="eg-dialog-msg" id="egDialogMsg"></p>' +
                '  <div class="eg-dialog-actions">' +
                '    <button type="button" class="eg-dialog-btn is-cancel">Vazgeç</button>' +
                '    <button type="button" class="eg-dialog-btn is-ok">Tamam</button>' +
                '  </div>' +
                '</div>';
            document.body.appendChild(root);

            box = root.querySelector('.eg-dialog');
            icon = root.querySelector('.eg-dialog-icon');
            title = root.querySelector('.eg-dialog-title');
            subject = root.querySelector('.eg-dialog-subject');
            msg = root.querySelector('.eg-dialog-msg');
            okBtn = root.querySelector('.is-ok');
            cancelBtn = root.querySelector('.is-cancel');

            okBtn.addEventListener('click', function () { close(true); });
            cancelBtn.addEventListener('click', function () { close(false); });
            root.querySelector('[data-close]').addEventListener('click', function () { close(false); });

            root.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { e.preventDefault(); close(false); }
                if (e.key === 'Tab') {
                    // Odak pencerenin disina cikmasin.
                    var items = isAlert ? [okBtn] : [cancelBtn, okBtn];
                    var i = items.indexOf(document.activeElement);
                    e.preventDefault();
                    items[(i + (e.shiftKey ? items.length - 1 : 1)) % items.length].focus();
                }
            });
        }

        function open(opts, alertMode) {
            if (!root) build();
            if (resolver) close(false);

            opts = opts || {};
            isAlert = !!alertMode;
            var tone = opts.tone || (isAlert ? 'info' : 'warning');

            box.className = 'eg-dialog tone-' + tone;
            icon.innerHTML = svg(opts.icon || tone, 26);
            title.textContent = opts.title || (isAlert ? 'Bilgi' : 'Emin misiniz?');
            subject.textContent = opts.subject || '';
            subject.hidden = !opts.subject;
            msg.textContent = opts.message || '';
            msg.hidden = !opts.message;
            okBtn.textContent = opts.okText || (isAlert ? 'Tamam' : 'Onayla');
            cancelBtn.textContent = opts.cancelText || 'Vazgeç';
            cancelBtn.hidden = isAlert;

            lastFocus = document.activeElement;
            root.hidden = false;
            document.documentElement.classList.add('eg-dialog-open');
            void root.offsetWidth; // gecis animasyonu icin once gorunur hale gelsin
            root.classList.add('is-open');

            // Geri alinamaz islemlerde varsayilan odak "Vazgec"te: yanlislikla Enter silmesin.
            (tone === 'danger' && !isAlert ? cancelBtn : okBtn).focus();

            return new Promise(function (resolve) { resolver = resolve; });
        }

        function close(result) {
            if (!resolver) return;
            var r = resolver;
            resolver = null;
            root.classList.remove('is-open');
            document.documentElement.classList.remove('eg-dialog-open');
            setTimeout(function () { if (!resolver) root.hidden = true; }, 180);
            if (lastFocus && lastFocus.focus) lastFocus.focus();
            r(result);
        }

        return {
            confirm: function (opts) { return open(opts, false); },
            alert: function (opts) { return open(typeof opts === 'string' ? { message: opts } : opts, true); }
        };
    })();

    // ---------------- Kose bildirimleri ----------------

    var toast = (function () {
        var stack;
        var TITLES = { success: 'Tamamlandı', error: 'Bir sorun oluştu', warning: 'Dikkat', info: 'Bilgi' };

        function show(opts) {
            if (typeof opts === 'string') opts = { message: opts };
            if (!stack) {
                stack = el('div', 'eg-toast-stack');
                stack.setAttribute('aria-live', 'polite');
                document.body.appendChild(stack);
            }

            var tone = opts.tone || 'info';
            var timeout = opts.timeout != null ? opts.timeout : (tone === 'error' ? 9000 : tone === 'warning' ? 7000 : 5000);

            var t = el('div', 'eg-toast tone-' + tone);
            t.setAttribute('role', tone === 'error' ? 'alert' : 'status');
            t.innerHTML =
                '<span class="eg-toast-icon">' + svg(tone === 'error' ? 'error' : tone, 18) + '</span>' +
                '<div class="eg-toast-body"><div class="eg-toast-title"></div><div class="eg-toast-msg"></div></div>' +
                '<button type="button" class="eg-toast-close" aria-label="Kapat">×</button>' +
                '<span class="eg-toast-bar"></span>';
            t.querySelector('.eg-toast-title').textContent = opts.title || TITLES[tone] || '';
            // html yalnizca sunucunun urettigi (Razor ile kodlanmis) icerik icin kullanilir.
            if (opts.html) t.querySelector('.eg-toast-msg').innerHTML = opts.html;
            else t.querySelector('.eg-toast-msg').textContent = opts.message || '';
            stack.appendChild(t);
            void t.offsetWidth;
            t.classList.add('is-in');

            var timer = null, remaining = timeout, started;
            var bar = t.querySelector('.eg-toast-bar');

            function dismiss() {
                clearTimeout(timer);
                t.classList.remove('is-in');
                t.classList.add('is-out');
                setTimeout(function () { t.remove(); }, 250);
            }
            function run() {
                if (!remaining) return;
                started = Date.now();
                bar.style.transition = 'transform ' + remaining + 'ms linear';
                bar.style.transform = 'scaleX(0)';
                timer = setTimeout(dismiss, remaining);
            }
            function pause() {
                if (!timer) return;
                clearTimeout(timer);
                timer = null;
                remaining = Math.max(0, remaining - (Date.now() - started));
                var scale = remaining / timeout;
                bar.style.transition = 'none';
                bar.style.transform = 'scaleX(' + scale + ')';
            }

            t.querySelector('.eg-toast-close').addEventListener('click', dismiss);
            t.addEventListener('mouseenter', pause);
            t.addEventListener('mouseleave', run);
            if (timeout) setTimeout(run, 30);
            else bar.hidden = true;

            return { dismiss: dismiss };
        }

        return {
            show: show,
            success: function (m, o) { return show(Object.assign({ tone: 'success', message: m }, o)); },
            error: function (m, o) { return show(Object.assign({ tone: 'error', message: m }, o)); },
            info: function (m, o) { return show(Object.assign({ tone: 'info', message: m }, o)); },
            warning: function (m, o) { return show(Object.assign({ tone: 'warning', message: m }, o)); }
        };
    })();

    window.egDialog = dialog;
    window.egToast = toast;

    // ---------------- data-confirm: form gonderiminden once onay ----------------

    function confirmOptions(src) {
        var d = src.dataset;
        return {
            message: d.confirm,
            title: d.confirmTitle,
            okText: d.confirmOk,
            tone: d.confirmTone || 'warning',
            subject: d.confirmSubject
        };
    }

    // Yakalama asamasi: diger submit dinleyicileri (buton "bekleniyor" durumu vb.)
    // onaydan once calismasin.
    document.addEventListener('submit', function (e) {
        var form = e.target;
        var btn = e.submitter;
        var src = btn && btn.dataset && btn.dataset.confirm != null ? btn
            : form.dataset.confirm != null ? form : null;

        if (!src || form.dataset.egConfirmed === '1') return;

        e.preventDefault();
        e.stopImmediatePropagation();

        dialog.confirm(confirmOptions(src)).then(function (ok) {
            if (!ok) return;
            form.dataset.egConfirmed = '1';
            try {
                if (btn && form.requestSubmit) form.requestSubmit(btn);
                else if (form.requestSubmit) form.requestSubmit();
                else form.submit();
            } finally {
                setTimeout(function () { delete form.dataset.egConfirmed; }, 0);
            }
        });
    }, true);

    // ---------------- sunucu bildirimleri -> kose bildirimi ----------------

    function flashToToasts() {
        var delay = 0;
        document.querySelectorAll('[data-flash]').forEach(function (n) {
            var text = n.textContent.replace(/\s+/g, ' ').trim();
            if (text) {
                var opts = {
                    tone: n.dataset.flash || 'info',
                    title: n.dataset.flashTitle,
                    html: n.innerHTML.trim()
                };
                if (n.dataset.flashTimeout != null) opts.timeout = parseInt(n.dataset.flashTimeout, 10);
                // Birden fazla bildirim art arda kaysin.
                setTimeout(function () { toast.show(opts); }, delay);
                delay += 180;
            }
            n.remove();
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', flashToToasts);
    else flashToToasts();

    // ---------------- açık / koyu tema ----------------

    function bindThemeToggle() {
        var btn = document.getElementById('themeToggle');
        if (!btn) return;

        btn.addEventListener('click', function () {
            var root = document.documentElement;
            var dark = root.getAttribute('data-theme') !== 'dark';
            root.setAttribute('data-theme', dark ? 'dark' : 'light');
            try { localStorage.setItem('eg-theme', dark ? 'dark' : 'light'); } catch (e) { }
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', bindThemeToggle);
    else bindThemeToggle();
})();
