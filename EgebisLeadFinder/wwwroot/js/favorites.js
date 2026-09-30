// Favori yildizi: [data-fav-toggle] dugmeleri sayfa yenilenmeden favoriye ekler/cikarir.
(function () {
    function token() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function paint(btn, on) {
        btn.classList.toggle('is-on', on);
        btn.setAttribute('aria-pressed', on ? 'true' : 'false');
        btn.title = on ? 'Favorilerden çıkar' : 'Favorilere ekle';
        var label = btn.querySelector('.fav-star-label');
        if (label) label.textContent = on ? 'Favorilerde' : 'Favorilere ekle';
    }

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-fav-toggle]');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();
        if (btn.disabled) return;
        btn.disabled = true;

        var body = new FormData();
        body.append('__RequestVerificationToken', token());
        fetch(btn.getAttribute('data-fav-toggle'), { method: 'POST', body: body, headers: { Accept: 'application/json' } })
            .then(function (r) { if (!r.ok) throw new Error(); return r.json(); })
            .then(function (d) {
                // Ayni firmanin sayfadaki tum yildizlari birlikte guncellenir.
                document.querySelectorAll('[data-fav-toggle="' + btn.getAttribute('data-fav-toggle') + '"]')
                    .forEach(function (b) { paint(b, d.favorite); });
                btn.classList.add('just-toggled');
                setTimeout(function () { btn.classList.remove('just-toggled'); }, 450);
                if (window.egToast) egToast.success(d.favorite ? 'Favorilere eklendi.' : 'Favorilerden çıkarıldı.');
            })
            .catch(function () { if (window.egToast) egToast.error('Favori kaydedilemedi, sayfayı yenileyip tekrar deneyin.'); })
            .finally(function () { btn.disabled = false; });
    });
})();
