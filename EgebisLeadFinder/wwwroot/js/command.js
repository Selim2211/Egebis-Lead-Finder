/* Komut paleti (Ctrl/Cmd+K): menudeki sayfalar + hizli eylemler. Yalnizca istemci tarafi. */
(function () {
    'use strict';

    var root = document.getElementById('egCmd');
    if (!root) return;
    var input = document.getElementById('egCmdInput');
    var list = document.getElementById('egCmdList');
    var opener = null, items = [], sel = 0;

    function norm(s) {
        return (s || '').toLocaleLowerCase('tr').replace(/ı/g, 'i').replace(/ğ/g, 'g').replace(/ü/g, 'u')
            .replace(/ş/g, 's').replace(/ö/g, 'o').replace(/ç/g, 'c');
    }

    function pages() {
        var out = [];
        document.querySelectorAll('.eg-side-link').forEach(function (a) {
            var label = a.querySelector('.eg-side-label');
            if (label) out.push({ title: label.textContent.trim(), hint: 'Sayfa', href: a.getAttribute('href') });
        });
        document.querySelectorAll('.nav-user-menu a.dropdown-item').forEach(function (a) {
            out.push({ title: a.textContent.trim(), hint: 'Hesap', href: a.getAttribute('href') });
        });
        return out;
    }

    function actions(q) {
        var out = [];
        var searchHref = (document.querySelector('.eg-side-link[href*="/Company/Search"]') || {}).getAttribute && document.querySelector('.eg-side-link[href*="/Company/Search"]').getAttribute('href');
        if (q && q.length >= 2 && searchHref) {
            out.push({ title: 'Firma ara: ' + q, hint: 'Eylem', href: searchHref + '#name=' + encodeURIComponent(q), name: q });
        }
        out.push({ title: 'Açık / koyu temayı değiştir', hint: 'Eylem', run: function () { var t = document.getElementById('themeToggle'); if (t) t.click(); } });
        return out;
    }

    function render(q) {
        var nq = norm(q);
        var all = pages().concat(actions(q.trim()));
        items = all.filter(function (it) { return !nq || norm(it.title).indexOf(nq) !== -1 || it.title.indexOf('Firma ara:') === 0; });
        sel = 0;
        list.innerHTML = '';
        if (!items.length) {
            var li = document.createElement('li'); li.className = 'eg-cmd-empty'; li.textContent = 'Sonuç yok'; list.appendChild(li); return;
        }
        items.forEach(function (it, i) {
            var li = document.createElement('li');
            li.className = 'eg-cmd-item' + (i === 0 ? ' is-sel' : '');
            li.setAttribute('role', 'option');
            var t = document.createElement('span'); t.textContent = it.title;
            var h = document.createElement('small'); h.textContent = it.hint;
            li.appendChild(t); li.appendChild(h);
            li.addEventListener('click', function () { go(it); });
            li.addEventListener('mousemove', function () { select(i); });
            list.appendChild(li);
        });
    }

    function select(i) {
        var nodes = list.querySelectorAll('.eg-cmd-item');
        if (!nodes.length) return;
        sel = (i + nodes.length) % nodes.length;
        nodes.forEach(function (n, k) { n.classList.toggle('is-sel', k === sel); });
        nodes[sel].scrollIntoView({ block: 'nearest' });
    }

    function fillName(q) {
        var box = document.getElementById('companyNameInput');
        if (!box) return false;
        box.value = q; box.focus(); box.scrollIntoView({ block: 'center' });
        return true;
    }

    function go(it) {
        close();
        if (it.run) { it.run(); return; }
        if (it.name && fillName(it.name)) return;       // zaten Firma Ara sayfasindayiz
        if (it.href) window.location.href = it.href;
    }

    function open() {
        opener = document.activeElement;
        root.hidden = false;
        input.value = '';
        render('');
        input.focus();
    }
    function close() {
        root.hidden = true;
        if (opener && opener.focus) opener.focus();
    }

    // Palet "Firma ara: ..." ile Firma Ara sayfasina getirdiyse ad kutusunu doldur.
    (function () {
        var m = /^#name=(.+)$/.exec(window.location.hash || '');
        if (m) { try { fillName(decodeURIComponent(m[1])); } catch (e) { } }
    })();

    input.addEventListener('input', function () { render(input.value); });
    input.addEventListener('keydown', function (e) {
        if (e.key === 'ArrowDown') { e.preventDefault(); select(sel + 1); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); select(sel - 1); }
        else if (e.key === 'Enter') { e.preventDefault(); if (items[sel]) go(items[sel]); }
        else if (e.key === 'Tab') { e.preventDefault(); }   // odak palette kalir
    });
    root.querySelectorAll('[data-cmd-close]').forEach(function (n) { n.addEventListener('click', close); });
    document.querySelectorAll('[data-cmd-open]').forEach(function (n) { n.addEventListener('click', open); });
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); root.hidden ? open() : close(); }
        else if (e.key === 'Escape' && !root.hidden) { close(); }
    });
})();
