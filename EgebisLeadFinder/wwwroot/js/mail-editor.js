// E-posta editoru: metne gorsel yapistirma/surukleme, hizalama ve boyutlandirma.
// Gonderimde editorun HTML'i (bodyHtml) ve duz metni (body) forma yazilir; sunucu
// HTML'i temizler ve /Email/Image/{id} gorsellerini e-postaya gomer.
(function () {
    var wrap = document.getElementById('mailEditor');
    if (!wrap) return;

    var editor = document.getElementById('bodyEditor');
    var bar = document.getElementById('imgBar');
    var barSize = document.getElementById('imgBarSize');
    var handle = document.getElementById('imgHandle');
    var dropHint = document.getElementById('mailDrop');
    var errorBox = document.getElementById('imgError');
    var lib = document.getElementById('imgLib');
    var form = wrap.closest('form');
    var token = form.querySelector('input[name="__RequestVerificationToken"]').value;
    var urls = { upload: wrap.dataset.uploadUrl, remove: wrap.dataset.removeUrl };

    var MAX_BYTES = 2 * 1024 * 1024;
    var TYPES = ['image/png', 'image/jpeg', 'image/gif'];
    var savedRange = null;
    var selected = null;

    // ---- yardimcilar ----

    function showError(msg) {
        if (msg && window.egToast) { window.egToast.error(msg); return; }
        errorBox.textContent = msg || '';
        errorBox.hidden = !msg;
    }

    function esc(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    async function post(url, fd) {
        fd.append('__RequestVerificationToken', token);
        var res = await fetch(url, { method: 'POST', body: fd, headers: { 'Accept': 'application/json' } });
        var data = null;
        try { data = await res.json(); } catch (e) { }
        if (!res.ok) throw new Error((data && data.error) || 'İşlem başarısız (' + res.status + ').');
        return data;
    }

    function isOurImage(src) {
        return /\/Email\/Image\/\d+/i.test(src || '');
    }

    // ---- imlec konumu (kutuphane butonuna tiklayinca kaybolmasin) ----

    document.addEventListener('selectionchange', function () {
        var s = getSelection();
        if (s.rangeCount && editor.contains(s.anchorNode)) savedRange = s.getRangeAt(0).cloneRange();
    });

    function restoreRange() {
        editor.focus();
        var s = getSelection();
        s.removeAllRanges();
        if (savedRange && editor.contains(savedRange.startContainer)) {
            s.addRange(savedRange);
        } else {
            var r = document.createRange();
            r.selectNodeContents(editor);
            r.collapse(false);
            s.addRange(r);
        }
    }

    // ---- gorsel ekleme ----

    function imgTag(url, name, width) {
        return '<img src="' + esc(url) + '" alt="' + esc(name) + '" width="' + width + '" '
            + 'style="width:' + width + 'px;max-width:100%;height:auto;vertical-align:middle;margin:0 4px">';
    }

    function insertImage(url, name) {
        var probe = new Image();
        probe.onload = function () {
            var w = Math.min(probe.naturalWidth || 160, 200);
            restoreRange();
            // insertHTML Chrome'da stili yeniden yaziyor; dugumu dogrudan ekliyoruz.
            var holder = document.createElement('div');
            holder.innerHTML = imgTag(url, name, w);
            var img = holder.firstChild;
            var s = getSelection();
            var r = s.getRangeAt(0);
            r.deleteContents();
            r.insertNode(img);
            r.setStartAfter(img);
            r.collapse(true);
            s.removeAllRanges();
            s.addRange(r);
            savedRange = r.cloneRange();
            editor.dispatchEvent(new Event('input', { bubbles: true }));
        };
        probe.onerror = function () { showError('Görsel yüklenemedi.'); };
        probe.src = url;
    }

    function validate(file) {
        if (TYPES.indexOf(file.type) < 0) return 'Sadece PNG, JPG veya GIF eklenebilir.';
        if (file.size > MAX_BYTES) return 'Görsel en fazla 2 MB olabilir.';
        return null;
    }

    async function upload(file, inLibrary) {
        var err = validate(file);
        if (err) throw new Error(err);
        var fd = new FormData();
        var base = (file.name || '').replace(/\.[^.]+$/, '');
        fd.append('file', file, file.name || 'gorsel.png');
        fd.append('name', !base || base === 'image' ? 'Yapıştırılan görsel' : base);
        fd.append('inLibrary', inLibrary ? 'true' : 'false');
        return post(urls.upload, fd);
    }

    async function uploadAndInsert(files) {
        showError(null);
        wrap.classList.add('is-busy');
        try {
            for (var i = 0; i < files.length; i++) {
                var img = await upload(files[i], false);
                insertImage(img.url, img.name);
            }
        } catch (e) {
            showError(e.message);
        } finally {
            wrap.classList.remove('is-busy');
        }
    }

    function imageFiles(dt) {
        return dt ? Array.prototype.filter.call(dt.files || [], function (f) { return f.type.indexOf('image/') === 0; }) : [];
    }

    // Disaridan yapistirilan HTML'den sadece metin + bizim gorsellerimiz kalir
    // (Word/web sayfasi stilleri e-postayi bozmasin).
    function cleanPastedHtml(html) {
        var doc = new DOMParser().parseFromString(html, 'text/html');
        var out = '';
        (function walk(node) {
            node.childNodes.forEach(function (n) {
                if (n.nodeType === 3) { out += esc(n.textContent); return; }
                if (n.nodeType !== 1) return;
                var tag = n.tagName;
                if (tag === 'SCRIPT' || tag === 'STYLE') return;
                if (tag === 'BR') { out += '<br>'; return; }
                if (tag === 'IMG') {
                    if (isOurImage(n.getAttribute('src'))) {
                        var w = parseInt(n.getAttribute('width'), 10) || 160;
                        out += imgTag(n.getAttribute('src'), n.getAttribute('alt') || '', w);
                    }
                    return;
                }
                var block = /^(DIV|P|LI|H[1-6]|TR|BLOCKQUOTE)$/.test(tag);
                walk(n);
                if (block) out += '<br>';
            });
        })(doc.body);
        return out;
    }

    editor.addEventListener('paste', function (e) {
        var cd = e.clipboardData;
        if (!cd) return;
        var files = imageFiles(cd);
        e.preventDefault();

        if (files.length) {
            uploadAndInsert(files);
            return;
        }

        var html = cd.getData('text/html');
        if (html && /<img/i.test(html)) {
            document.execCommand('insertHTML', false, cleanPastedHtml(html));
        } else {
            document.execCommand('insertText', false, cd.getData('text/plain'));
        }
    });

    // Dosya surukle-birak (editor icindeki gorseli tasima tarayicinin kendi davranisi).
    function hasFiles(e) {
        return e.dataTransfer && Array.prototype.indexOf.call(e.dataTransfer.types || [], 'Files') >= 0;
    }

    editor.addEventListener('dragover', function (e) {
        if (!hasFiles(e)) return;
        e.preventDefault();
        dropHint.hidden = false;
    });
    editor.addEventListener('dragleave', function () { dropHint.hidden = true; });
    editor.addEventListener('drop', function (e) {
        dropHint.hidden = true;
        var files = imageFiles(e.dataTransfer);
        if (!files.length) { setTimeout(deselect, 0); return; }
        e.preventDefault();
        if (document.caretRangeFromPoint) {
            var r = document.caretRangeFromPoint(e.clientX, e.clientY);
            if (r && editor.contains(r.startContainer)) savedRange = r;
        }
        uploadAndInsert(files);
    });

    document.getElementById('inlineFile').addEventListener('change', function () {
        var files = Array.prototype.slice.call(this.files);
        this.value = '';
        if (files.length) uploadAndInsert(files);
    });

    // ---- bicim butonlari ----

    wrap.querySelectorAll('[data-cmd]').forEach(function (btn) {
        btn.addEventListener('mousedown', function (e) { e.preventDefault(); });
        btn.addEventListener('click', function () {
            editor.focus();
            document.execCommand(btn.dataset.cmd, false, null);
        });
    });

    // ---- secili gorsel: hizalama, boyut, silme ----

    function widthOf(img) {
        return parseInt(img.getAttribute('width'), 10) || Math.round(img.getBoundingClientRect().width) || 160;
    }

    function alignOf(img) {
        if (img.style.float === 'left') return 'left';
        if (img.style.float === 'right') return 'right';
        if (img.style.display === 'block') return 'center';
        return 'inline';
    }

    function applyImg(img, align, width) {
        var w = Math.max(24, Math.min(600, Math.round(width)));
        var css = 'width:' + w + 'px;max-width:100%;height:auto;';
        if (align === 'left') css += 'float:left;margin:4px 16px 8px 0;';
        else if (align === 'right') css += 'float:right;margin:4px 0 8px 16px;';
        else if (align === 'center') css += 'display:block;margin:8px auto;';
        else css += 'vertical-align:middle;margin:0 4px;';
        img.setAttribute('width', w);
        img.style.cssText = css;
        placeTools();
    }

    function placeTools() {
        if (!selected || !editor.contains(selected)) { deselect(); return; }
        var w = wrap.getBoundingClientRect();
        var r = selected.getBoundingClientRect();
        var top = r.top - w.top - bar.offsetHeight - 8;
        if (top < 4) top = r.bottom - w.top + 8;
        var left = Math.max(4, Math.min(r.left - w.left, w.width - bar.offsetWidth - 4));
        bar.style.top = top + 'px';
        bar.style.left = left + 'px';
        handle.style.top = (r.bottom - w.top - 7) + 'px';
        handle.style.left = (r.right - w.left - 7) + 'px';
        barSize.textContent = widthOf(selected) + 'px';
        var align = alignOf(selected);
        bar.querySelectorAll('[data-align]').forEach(function (b) {
            b.classList.toggle('is-on', b.dataset.align === align);
        });
    }

    function select(img) {
        if (selected && selected !== img) selected.classList.remove('is-selected');
        selected = img;
        img.classList.add('is-selected');
        bar.hidden = false;
        handle.hidden = false;
        placeTools();
    }

    function deselect() {
        if (selected) selected.classList.remove('is-selected');
        selected = null;
        bar.hidden = true;
        handle.hidden = true;
    }

    editor.addEventListener('click', function (e) {
        if (e.target.tagName === 'IMG') select(e.target);
        else deselect();
    });

    document.addEventListener('mousedown', function (e) {
        if (!selected) return;
        if (wrap.contains(e.target) && (e.target === selected || bar.contains(e.target) || e.target === handle)) return;
        deselect();
    });

    editor.addEventListener('keydown', function (e) {
        if (!selected) return;
        if (e.key === 'Delete' || e.key === 'Backspace') {
            e.preventDefault();
            selected.remove();
            deselect();
        } else if (e.key === 'Escape') {
            deselect();
        }
    });

    editor.addEventListener('input', function () { if (selected) placeTools(); });
    window.addEventListener('resize', function () { if (selected) placeTools(); });
    window.addEventListener('scroll', function () { if (selected) placeTools(); }, true);

    bar.addEventListener('mousedown', function (e) { e.preventDefault(); });
    bar.addEventListener('click', function (e) {
        var btn = e.target.closest('button');
        if (!btn || !selected) return;
        if (btn.dataset.align) {
            applyImg(selected, btn.dataset.align, widthOf(selected));
        } else if (btn.dataset.size) {
            applyImg(selected, alignOf(selected), widthOf(selected) + 20 * parseInt(btn.dataset.size, 10));
        } else if (btn.dataset.action === 'remove') {
            selected.remove();
            deselect();
        }
    });

    // Kose tutamaci ile boyutlandirma.
    handle.addEventListener('pointerdown', function (e) {
        if (!selected) return;
        e.preventDefault();
        var img = selected;
        var startX = e.clientX;
        var startW = widthOf(img);
        var dir = alignOf(img) === 'right' ? -1 : 1;
        var maxW = editor.clientWidth - 24;
        handle.setPointerCapture(e.pointerId);

        function move(ev) {
            applyImg(img, alignOf(img), Math.min(maxW, startW + dir * (ev.clientX - startX)));
        }
        function up() {
            handle.removeEventListener('pointermove', move);
            handle.removeEventListener('pointerup', up);
        }
        handle.addEventListener('pointermove', move);
        handle.addEventListener('pointerup', up);
    });

    // ---- kayitli gorseller ----

    function chipHtml(img) {
        return '<div class="img-chip" data-id="' + img.id + '" data-name="' + esc(img.name) + '" data-url="' + esc(img.url) + '">'
            + '<button type="button" class="img-chip-insert" title="Metne ekle">'
            + '<span class="img-chip-thumb"><img src="' + esc(img.url) + '" alt="" /></span>'
            + '<span class="img-chip-name">' + esc(img.name) + '</span></button>'
            + '<button type="button" class="img-chip-del" title="Listeden kaldır" aria-label="Listeden kaldır">×</button></div>';
    }

    lib.addEventListener('mousedown', function (e) {
        if (e.target.closest('.img-chip-insert')) e.preventDefault();
    });

    lib.addEventListener('click', async function (e) {
        var chip = e.target.closest('.img-chip');
        if (!chip || !chip.dataset.id) return;

        if (e.target.closest('.img-chip-insert')) {
            insertImage(chip.dataset.url, chip.dataset.name);
            return;
        }

        if (e.target.closest('.img-chip-del')) {
            var ok = await window.egDialog.confirm({
                tone: 'warning',
                title: 'Görsel listeden kaldırılsın mı?',
                subject: chip.dataset.name,
                message: 'Metne eklenmiş olanlar ve gönderilmiş e-postalar etkilenmez.',
                okText: 'Kaldır'
            });
            if (!ok) return;
            var fd = new FormData();
            fd.append('id', chip.dataset.id);
            try {
                await post(urls.remove, fd);
                chip.remove();
            } catch (err) { showError(err.message); }
        }
    });

    document.getElementById('libFile').addEventListener('change', async function () {
        var file = this.files[0];
        this.value = '';
        if (!file) return;
        showError(null);
        var addChip = lib.querySelector('.img-chip-add');
        addChip.classList.add('is-busy');
        try {
            var img = await upload(file, true);
            addChip.insertAdjacentHTML('beforebegin', chipHtml(img));
        } catch (err) {
            showError(err.message);
        } finally {
            addChip.classList.remove('is-busy');
        }
    });

    // ---- gonderim ve kopyalama ----

    function plainText() {
        return editor.innerText.replace(/ /g, ' ').replace(/\n{3,}/g, '\n\n').trim();
    }

    function syncFields() {
        deselect();
        document.getElementById('bodyHtml').value = editor.innerHTML;
        document.getElementById('bodyText').value = plainText();
    }

    form.addEventListener('submit', syncFields);

    // Diger ekranlar (Taslak Duzenleyici) icin: imlece metin ekleme ve alan senkronu.
    window.mailEditor = {
        element: editor,
        insertText: function (text) {
            restoreRange();
            document.execCommand('insertText', false, text);
        },
        sync: syncFields,
        plainText: plainText
    };

    function blobToDataUrl(blob) {
        return new Promise(function (resolve, reject) {
            var fr = new FileReader();
            fr.onload = function () { resolve(fr.result); };
            fr.onerror = reject;
            fr.readAsDataURL(blob);
        });
    }

    // Outlook'a yapistirinca gorseller de gelsin diye gorseller data URL olarak kopyalanir.
    async function copyHtml() {
        var clone = editor.cloneNode(true);
        var imgs = clone.querySelectorAll('img');
        for (var i = 0; i < imgs.length; i++) {
            imgs[i].classList.remove('is-selected');
            var blob = await fetch(imgs[i].src).then(function (r) { return r.blob(); });
            imgs[i].src = await blobToDataUrl(blob);
        }
        return '<div style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.6;color:#1f2937">'
            + clone.innerHTML + '</div>';
    }

    var copyButton = document.getElementById('copyButton');
    if (copyButton) copyButton.addEventListener('click', async function () {
        var button = this;
        deselect();
        var text = plainText();

        try {
            var html = await copyHtml();
            await navigator.clipboard.write([new ClipboardItem({
                'text/html': new Blob([html], { type: 'text/html' }),
                'text/plain': new Blob([text], { type: 'text/plain' })
            })]);
            button.textContent = 'KOPYALANDI';
        } catch (err) {
            // Guvenli olmayan baglantida clipboard API calismaz; metni secip kopyalatalim.
            var r = document.createRange();
            r.selectNodeContents(editor);
            var s = getSelection();
            s.removeAllRanges();
            s.addRange(r);
            button.textContent = 'Ctrl+C ile kopyalayın';
        }

        setTimeout(function () { button.textContent = 'KOPYALA'; }, 2500);
    });
})();
