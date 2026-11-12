// Haber terminali (Özet): dönen manşet, liderlik tablosu, seçili firma ve dönen dünya.
(function () {
    "use strict";
    var reduce = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    // ---------- Manşet: sırayla dönen önemli haberler ----------
    var hero = document.querySelector("[data-hero]");
    if (hero) {
        var slides = hero.querySelectorAll(".tx-slide");
        var dots = hero.querySelectorAll("[data-slide]");
        var cur = 0, timer = null;
        function show(i) {
            if (!slides.length) return;
            cur = (i + slides.length) % slides.length;
            slides.forEach(function (s, k) {
                var on = k === cur;
                s.classList.toggle("is-on", on);
                s.setAttribute("aria-hidden", on ? "false" : "true");
                var a = s.querySelector("a"); if (a) a.tabIndex = on ? 0 : -1;
            });
            dots.forEach(function (d, k) { d.classList.toggle("is-on", k === cur); });
        }
        function start() { if (!reduce && slides.length > 1) { stop(); timer = setInterval(function () { show(cur + 1); }, 8000); } }
        function stop() { if (timer) clearInterval(timer); timer = null; }
        dots.forEach(function (d) { d.addEventListener("click", function () { show(+d.dataset.slide); start(); }); });
        hero.addEventListener("mouseenter", stop);
        hero.addEventListener("mouseleave", start);
        hero.addEventListener("focusin", stop);
        start();
    }

    // ---------- Liderlik tablosu: Günlük / Haftalık ----------
    document.querySelectorAll("[data-board-btn]").forEach(function (btn) {
        btn.addEventListener("click", function () {
            var which = btn.dataset.boardBtn;
            document.querySelectorAll("[data-board-btn]").forEach(function (b) { b.classList.toggle("is-on", b === btn); });
            document.querySelectorAll("[data-board]").forEach(function (ol) { ol.hidden = ol.dataset.board !== which; });
        });
    });

    // ---------- Satıra tıklayınca sağ panelde o firma ----------
    document.querySelectorAll("[data-pick]").forEach(function (row) {
        row.addEventListener("click", function () {
            var key = row.dataset.pick;
            document.querySelectorAll("[data-pick]").forEach(function (r) { r.classList.toggle("is-sel", r.dataset.pick === key); });
            document.querySelectorAll("[data-co]").forEach(function (p) { p.hidden = p.dataset.co !== key; });
        });
    });

    // ---------- Dönen dünya ----------
    var canvas = document.querySelector(".tx-globe");
    if (!canvas || !canvas.getContext) return;
    var ctx = canvas.getContext("2d");
    var D = Math.PI / 180;

    // Kıtaları yaklaşık çizen daireler: [enlem, boylam, yarıçap(derece)]
    var land = [
        [48, -100, 22], [60, -115, 18], [35, -95, 14], [20, -100, 8], [12, -86, 5], [65, -150, 9], [72, -40, 9], [55, -70, 10],
        [-8, -60, 16], [-25, -60, 12], [-40, -68, 7], [5, -70, 9],
        [50, 10, 9], [45, 25, 9], [60, 20, 9], [40, -4, 6], [54, -2, 3.5], [64, 15, 6],
        [8, 20, 19], [20, 10, 14], [-12, 28, 14], [-25, 25, 9], [15, 38, 7],
        [24, 45, 8], [39, 35, 4], [32, 55, 8], [22, 78, 9], [48, 70, 14], [55, 95, 18], [62, 125, 16], [35, 105, 13], [25, 105, 8],
        [15, 102, 6], [36, 138, 4], [-2, 113, 6], [-5, 140, 5], [10, 123, 4],
        [-25, 134, 12], [-38, 145, 4], [-42, 172, 3]
    ];
    function isLand(lat, lon) {
        for (var i = 0; i < land.length; i++) {
            var c = land[i];
            var d = Math.acos(Math.max(-1, Math.min(1,
                Math.sin(lat * D) * Math.sin(c[0] * D) + Math.cos(lat * D) * Math.cos(c[0] * D) * Math.cos((lon - c[1]) * D)))) / D;
            var edge = c[2] * (0.82 + 0.18 * Math.sin(lat * 0.7 + lon * 0.45) * Math.cos(lon * 0.3 - lat * 0.2));
            if (d < edge) return true;
        }
        return false;
    }
    function vec(lat, lon) { return [Math.cos(lat * D) * Math.sin(lon * D), Math.sin(lat * D), Math.cos(lat * D) * Math.cos(lon * D)]; }

    // Fibonacci küresi üzerinde nokta bulutu; karadakiler parlak, denizdekiler çok soluk.
    var pts = [], N = 7000, golden = Math.PI * (3 - Math.sqrt(5));
    for (var i = 0; i < N; i++) {
        var y = 1 - (i / (N - 1)) * 2, r = Math.sqrt(1 - y * y), th = golden * i;
        var lat = Math.asin(y) / D, lon = Math.atan2(Math.cos(th) * r, Math.sin(th) * r) / D;
        var isL = isLand(lat, lon);
        if (isL || i % 5 === 0) pts.push({ v: [Math.cos(th) * r, y, Math.sin(th) * r], land: isL });
    }
    // Merkezler ve bağlantı yayları (İzmir/İstanbul çıkışlı).
    var cities = [[38.4, 27.1], [41.0, 29.0], [39.9, 32.8], [51.5, -0.1], [40.7, -74.0], [50.1, 8.7], [25.2, 55.3], [31.2, 121.5], [1.35, 103.8], [48.9, 2.35], [55.75, 37.6], [35.7, 139.7]];
    var cityV = cities.map(function (c) { return vec(c[0], c[1]); });
    var links = [[0, 3], [0, 5], [1, 4], [1, 6], [2, 10], [0, 7], [1, 9], [6, 8], [5, 11], [3, 4]];
    var arcs = links.map(function (l) {
        var a = cityV[l[0]], b = cityV[l[1]], seg = [], steps = 40;
        var dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2], ang = Math.acos(Math.max(-1, Math.min(1, dot)));
        for (var s = 0; s <= steps; s++) {
            var t = s / steps, sa = Math.sin((1 - t) * ang) / Math.sin(ang), sb = Math.sin(t * ang) / Math.sin(ang);
            var lift = 1 + Math.sin(Math.PI * t) * 0.12 * (ang / 1.2);
            seg.push([(a[0] * sa + b[0] * sb) * lift, (a[1] * sa + b[1] * sb) * lift, (a[2] * sa + b[2] * sb) * lift]);
        }
        return { seg: seg, off: Math.random() };
    });

    var W = 0, H = 0, dpr = 1, rot = -20 * D, tilt = 18 * D, last = 0, visible = true, raf = 0;
    function resize() {
        var rect = canvas.getBoundingClientRect();
        dpr = Math.min(window.devicePixelRatio || 1, 2);
        W = rect.width; H = rect.height;
        canvas.width = Math.max(1, Math.round(W * dpr)); canvas.height = Math.max(1, Math.round(H * dpr));
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        if (reduce || !visible) draw(0);
    }
    function proj(v, cx, cy, R) {
        var ct = Math.cos(rot), st = Math.sin(rot), x = v[0] * ct + v[2] * st, z = -v[0] * st + v[2] * ct;
        var ck = Math.cos(tilt), sk = Math.sin(tilt), y = v[1] * ck - z * sk, z2 = v[1] * sk + z * ck;
        return [cx + x * R, cy - y * R, z2];
    }
    function draw(t) {
        ctx.clearRect(0, 0, W, H);
        var R = Math.min(H * 0.8, W * 0.34), cx = W - R * 0.95, cy = H * 0.58;

        var glow = ctx.createRadialGradient(cx, cy, R * 0.6, cx, cy, R * 1.35);
        glow.addColorStop(0, "rgba(16,185,129,0.10)"); glow.addColorStop(0.55, "rgba(56,189,248,0.07)"); glow.addColorStop(1, "rgba(56,189,248,0)");
        ctx.fillStyle = glow; ctx.fillRect(0, 0, W, H);
        var body = ctx.createRadialGradient(cx - R * 0.3, cy - R * 0.35, R * 0.1, cx, cy, R);
        body.addColorStop(0, "rgba(30,64,96,0.55)"); body.addColorStop(1, "rgba(6,14,26,0.85)");
        ctx.beginPath(); ctx.arc(cx, cy, R, 0, Math.PI * 2); ctx.fillStyle = body; ctx.fill();
        ctx.strokeStyle = "rgba(94,234,212,0.25)"; ctx.lineWidth = 1; ctx.stroke();

        // Enlem/boylam çizgileri
        ctx.lineWidth = 0.6; ctx.strokeStyle = "rgba(125,211,252,0.10)";
        for (var la = -60; la <= 60; la += 30) { line(function (k) { return vec(la, k); }, -180, 180, cx, cy, R); }
        for (var lo = 0; lo < 360; lo += 30) { line(function (k) { return vec(k, lo); }, -90, 90, cx, cy, R); }

        for (var i = 0; i < pts.length; i++) {
            var p = proj(pts[i].v, cx, cy, R);
            if (p[2] <= 0.02) continue;
            if (pts[i].land) {
                ctx.fillStyle = "rgba(" + (110 + 90 * p[2] | 0) + ",231,214," + (0.35 + 0.65 * p[2]).toFixed(2) + ")";
                var s = 0.9 + 1.3 * p[2]; ctx.fillRect(p[0] - s / 2, p[1] - s / 2, s, s);
            } else {
                ctx.fillStyle = "rgba(125,211,252," + (0.05 + 0.12 * p[2]).toFixed(2) + ")";
                ctx.fillRect(p[0] - 0.5, p[1] - 0.5, 1, 1);
            }
        }

        // Bağlantı yayları + üzerinde kayan ışık
        for (var a = 0; a < arcs.length; a++) {
            var seg = arcs[a].seg, prev = null;
            ctx.beginPath();
            for (var s2 = 0; s2 < seg.length; s2++) {
                var q = proj(seg[s2], cx, cy, R);
                if (q[2] > 0) { if (prev) ctx.lineTo(q[0], q[1]); else ctx.moveTo(q[0], q[1]); prev = q; } else prev = null;
            }
            ctx.strokeStyle = "rgba(52,211,153,0.35)"; ctx.lineWidth = 1; ctx.stroke();
            var head = ((t / 3200 + arcs[a].off) % 1) * (seg.length - 1) | 0, hq = proj(seg[head], cx, cy, R);
            if (hq[2] > 0) { ctx.beginPath(); ctx.arc(hq[0], hq[1], 2, 0, Math.PI * 2); ctx.fillStyle = "rgba(167,243,208,0.95)"; ctx.fill(); }
        }
        // Şehir noktaları (nabız halkası)
        for (var c = 0; c < cityV.length; c++) {
            var cp = proj(cityV[c], cx, cy, R);
            if (cp[2] <= 0.05) continue;
            var ph = ((t / 2000) + c * 0.17) % 1;
            ctx.beginPath(); ctx.arc(cp[0], cp[1], 2 + ph * 9, 0, Math.PI * 2);
            ctx.strokeStyle = "rgba(52,211,153," + (0.6 * (1 - ph) * cp[2]).toFixed(2) + ")"; ctx.lineWidth = 1; ctx.stroke();
            ctx.beginPath(); ctx.arc(cp[0], cp[1], c < 3 ? 2.6 : 1.8, 0, Math.PI * 2);
            ctx.fillStyle = c < 3 ? "#fbbf24" : "#6ee7b7"; ctx.fill();
        }
    }
    function line(fn, from, to, cx, cy, R) {
        ctx.beginPath(); var on = false;
        for (var k = from; k <= to; k += 6) {
            var p = proj(fn(k), cx, cy, R);
            if (p[2] > 0) { if (on) ctx.lineTo(p[0], p[1]); else ctx.moveTo(p[0], p[1]); on = true; } else on = false;
        }
        ctx.stroke();
    }
    function frame(t) {
        if (!visible) { raf = 0; return; }
        if (last) rot += Math.min(t - last, 60) * 0.00012;
        last = t;
        draw(t);
        raf = requestAnimationFrame(frame);
    }

    if (window.ResizeObserver) new ResizeObserver(resize).observe(canvas); else window.addEventListener("resize", resize);
    resize();
    if (reduce) { draw(0); return; }
    if (window.IntersectionObserver) {
        new IntersectionObserver(function (e) {
            visible = e[0].isIntersecting;
            if (visible && !raf) { last = 0; raf = requestAnimationFrame(frame); }
        }).observe(canvas);
    }
    raf = requestAnimationFrame(frame);
})();
