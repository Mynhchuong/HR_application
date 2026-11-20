/* ================================================================
   page-loading.js — màn "Đang tải dữ liệu..." dùng CHUNG cho mọi trang (_Layout + _LayoutExpat).
   Viết lại 2026-11-20 vì phản ánh loading treo / hiện lung tung:
   - Bấm Xuất Excel / tải mẫu: trang không chuyển nên loading treo mãi. Giờ server gắn cookie
     hr_dl cho mọi response tải file (Program.cs) → thấy cookie đổi là tắt + báo "Đã tải xong".
   - Trước chỉ gắn sự kiện cho thẻ <a> có sẵn lúc tải trang; link vẽ bằng JS về sau thì không,
     link javascript:/mailto:/tel:, Ctrl/⌘+click mở tab mới vẫn bật loading. Giờ bắt chung 1 chỗ.
   - Trang mở nhanh (<0,35s) không nháy loading nữa.
   - Lưới an toàn: loading tự tắt sau 30s nếu trang vẫn đứng yên — không bao giờ khoá màn hình mãi.
   Chỉ có MỘT loading toàn trang; trang nào cần thì gọi window.showLoading()/hideLoading().
   ================================================================ */
(function () {
    'use strict';
    var SHOW_DELAY = 350, SAFETY_MS = 30000, DL_MAX_MS = 120000;
    var showTimer = null, safetyTimer = null, dlTimer = null, dlStart = 0, dlCookie = null;
    // Link tải file: action có chữ Export/Download/Excel/GetFile, hoặc tên kết thúc bằng Template
    // (DownloadTemplate, StudentImportTemplate...). KHÔNG khớp trang thường như /NotiTemplate/Index.
    var FILE_RE = /(export|download|excel|getfile|\.xlsx|\.csv|\.pdf)|template$/i;

    function el() { return document.getElementById('pageLoadingOverlay'); }
    function setText(t) {
        var o = el(); if (!o) return;
        var d = o.querySelector('.fw-bold');
        if (d) { if (!d.dataset.def) d.dataset.def = d.textContent; d.textContent = t || d.dataset.def; }
    }
    function readDl() {
        var m = document.cookie.match(/(?:^|;\s*)hr_dl=([^;]*)/);
        return m ? m[1] : '';
    }

    function show(text) {
        clearTimeout(showTimer);
        setText(text);
        var o = el(); if (o) o.classList.add('show');
        clearTimeout(safetyTimer);
        safetyTimer = setTimeout(hide, SAFETY_MS);
        watchDownload();                 // trang tự gọi showLoading() rồi tải file cũng tự tắt được
    }
    function hide() {
        clearTimeout(showTimer); clearTimeout(safetyTimer); clearInterval(dlTimer);
        dlTimer = null;
        var o = el(); if (o) o.classList.remove('show');
        setText();
    }
    function showDelayed() {
        clearTimeout(showTimer);
        showTimer = setTimeout(function () { show(); }, SHOW_DELAY);
    }

    // Theo dõi cookie hr_dl: đổi giá trị = server vừa trả xong 1 file → tắt loading.
    function watchDownload() {
        if (dlTimer) return;
        dlCookie = readDl(); dlStart = Date.now();
        dlTimer = setInterval(function () {
            if (readDl() !== dlCookie) {
                hide();
                if (window.showToast) window.showToast(window.UI_LANG === 'ko' ? '다운로드 완료' : window.UI_LANG === 'en' ? 'Download complete' : 'Đã tải file xong', 'success');
            } else if (Date.now() - dlStart > DL_MAX_MS) {
                hide();
            }
        }, 400);
    }

    window.showLoading = function (text) { show(text); };
    window.hideLoading = hide;

    document.addEventListener('click', function (e) {
        // Đợi hết các handler khác chạy xong (handler gắn sau trên document có thể preventDefault để
        // tự xử lý bằng AJAX) rồi mới quyết định — trước đây loading vẫn bật dù trang không chuyển.
        setTimeout(function () { onLinkClick(e); }, 0);
    }, false);

    function onLinkClick(e) {
        if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        var a = e.target.closest && e.target.closest('a[href]');
        if (!a) return;
        if (a.target && a.target !== '_self') return;
        if (a.hasAttribute('download') || a.hasAttribute('data-bs-toggle') || a.hasAttribute('data-no-loading')
            || a.classList.contains('no-loading') || a.classList.contains('dropdown-toggle')) return;
        var href = a.getAttribute('href') || '';
        if (!href || href.charAt(0) === '#' || /^(javascript|mailto|tel|sms|blob|data):/i.test(href)) return;
        var url;
        try { url = new URL(a.href, location.href); } catch (err) { return; }
        if (url.origin !== location.origin) return;
        if (url.pathname === location.pathname && url.search === location.search && url.hash) return;   // chỉ nhảy tới mục trong trang

        if (FILE_RE.test(url.pathname)) {
            // Tải file: hiện ngay, chữ khác, tắt khi file về
            var lang = window.UI_LANG;
            show(lang === 'ko' ? '파일 생성 중...' : lang === 'en' ? 'Preparing file...' : 'Đang tạo file...');
        } else {
            showDelayed();
        }
    }

    window.addEventListener('load', hide);
    window.addEventListener('pageshow', function (e) { if (e.persisted) hide(); });   // quay lại từ bfcache
    window.addEventListener('popstate', hide);
})();
