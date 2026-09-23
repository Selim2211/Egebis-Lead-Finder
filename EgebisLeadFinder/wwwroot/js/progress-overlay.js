/* =============================================================================
   Egebis Lead Finder — ilerleme göstergesi
   Uzun süren işlemler (firma araması, ön araştırma / finansal analiz) arka planda
   çalışır. Bu dosya tam ekran bir katman açar, sunucudan yüzde ilerlemeyi yoklar
   ve iş bitince sonuç sayfasına yönlendirir.

   Kullanım: <form data-progress-start="/Company/StartSearch"
                   data-progress-title="..." data-progress-hint="...">
   JavaScript kapalıysa form normal POST'una düşer (kademeli iyileştirme).
   ============================================================================= */
(function () {
    "use strict";

    var POLL_MS = 900;
    var RADIUS = 52;
    var CIRC = 2 * Math.PI * RADIUS;

    function build() {
        var el = document.getElementById("eg-progress");
        if (el) return el;

        el = document.createElement("div");
        el.id = "eg-progress";
        el.className = "eg-progress";
        el.setAttribute("aria-live", "polite");
        el.innerHTML =
            '<div class="eg-progress__card" role="status">' +
                '<div class="eg-progress__title"></div>' +
                '<div class="eg-progress__ring">' +
                    '<svg viewBox="0 0 120 120" aria-hidden="true">' +
                        '<circle class="eg-progress__track" cx="60" cy="60" r="' + RADIUS + '"></circle>' +
                        '<circle class="eg-progress__bar" cx="60" cy="60" r="' + RADIUS + '"' +
                        ' stroke-dasharray="' + CIRC + '" stroke-dashoffset="' + CIRC + '"></circle>' +
                    "</svg>" +
                    '<span class="eg-progress__pct">%0</span>' +
                "</div>" +
                '<div class="eg-progress__stage"></div>' +
                '<div class="eg-progress__detail"></div>' +
                '<div class="eg-progress__hint"></div>' +
                '<div class="eg-progress__error" hidden></div>' +
                '<button type="button" class="btn btn-outline-secondary btn-sm eg-progress__close" hidden>Kapat</button>' +
            "</div>";

        document.body.appendChild(el);
        el.querySelector(".eg-progress__close").addEventListener("click", function () {
            close(el);
        });
        return el;
    }

    function setPercent(el, pct) {
        pct = Math.max(0, Math.min(100, pct));
        el.querySelector(".eg-progress__bar").style.strokeDashoffset = CIRC * (1 - pct / 100);
        el.querySelector(".eg-progress__pct").textContent = "%" + Math.round(pct);
    }

    function open(opts) {
        var el = build();
        el.querySelector(".eg-progress__title").textContent = opts.title || "İşlem yürütülüyor";
        el.querySelector(".eg-progress__hint").textContent = opts.hint || "";
        el.querySelector(".eg-progress__stage").textContent = "Başlatılıyor…";
        el.querySelector(".eg-progress__detail").textContent = "";
        var err = el.querySelector(".eg-progress__error");
        err.textContent = "";
        err.hidden = true;
        el.querySelector(".eg-progress__close").hidden = true;
        el.classList.remove("is-error");
        setPercent(el, 0);
        el.classList.add("is-open");
        document.body.classList.add("eg-progress-lock");
        return el;
    }

    function close(el) {
        el.classList.remove("is-open");
        document.body.classList.remove("eg-progress-lock");
    }

    function fail(el, message) {
        el.classList.add("is-error");
        el.querySelector(".eg-progress__stage").textContent = "İşlem başarısız";
        el.querySelector(".eg-progress__detail").textContent = "";
        var err = el.querySelector(".eg-progress__error");
        err.textContent = message || "Bilinmeyen bir hata oluştu.";
        err.hidden = false;
        el.querySelector(".eg-progress__close").hidden = false;
    }

    function readJson(response) {
        return response.json()
            .catch(function () { return {}; })
            .then(function (data) { return { ok: response.ok, data: data }; });
    }

    function poll(el, url) {
        fetch(url, { headers: { Accept: "application/json" } })
            .then(readJson)
            .then(function (res) {
                if (!res.ok) {
                    fail(el, (res.data && res.data.error) || "Durum bilgisi alınamadı.");
                    return;
                }

                var d = res.data;
                if (typeof d.percent === "number") setPercent(el, d.percent);
                if (d.stage) el.querySelector(".eg-progress__stage").textContent = d.stage;
                el.querySelector(".eg-progress__detail").textContent = d.detail || "";

                if (d.state === "done") {
                    setPercent(el, 100);
                    el.querySelector(".eg-progress__stage").textContent = "Tamamlandı, yönlendiriliyor…";
                    window.location.href = d.resultUrl || window.location.href;
                    return;
                }
                if (d.state === "failed") {
                    fail(el, d.error);
                    return;
                }
                window.setTimeout(function () { poll(el, url); }, POLL_MS);
            })
            .catch(function () {
                // Geçici ağ hatası: yoklamayı sürdür.
                window.setTimeout(function () { poll(el, url); }, POLL_MS);
            });
    }

    function start(opts) {
        var el = open(opts);
        // submitter: hangi butona basildiysa (ör. "Kaydet ve Ara") onun name/value'su da gitsin.
        var body = opts.form
            ? (opts.submitter ? new FormData(opts.form, opts.submitter) : new FormData(opts.form))
            : (opts.body || null);

        fetch(opts.url, { method: "POST", body: body, headers: { Accept: "application/json" } })
            .then(readJson)
            .then(function (res) {
                // Sunucu isi atladiysa (ör. firma bugun zaten arastirildi) beklemeye gerek yok.
                if (res.ok && res.data && res.data.skipped) {
                    window.location.href = res.data.resultUrl || window.location.href;
                    return;
                }

                if (!res.ok || !res.data || !res.data.progressUrl) {
                    fail(el, (res.data && res.data.error) || "İşlem başlatılamadı.");
                    return;
                }
                poll(el, res.data.progressUrl);
            })
            .catch(function () {
                fail(el, "Sunucuya ulaşılamadı.");
            });
    }

    window.EgebisProgress = { start: start };

    document.addEventListener("DOMContentLoaded", function () {
        var forms = document.querySelectorAll("form[data-progress-start]");
        Array.prototype.forEach.call(forms, function (form) {
            form.addEventListener("submit", function (e) {
                e.preventDefault();
                start({
                    url: form.getAttribute("data-progress-start"),
                    form: form,
                    submitter: e.submitter || null,
                    title: form.getAttribute("data-progress-title") || undefined,
                    hint: form.getAttribute("data-progress-hint") || undefined
                });
            });
        });
    });
})();
