/**
 * DocReader — tiện ích đọc tài liệu dài trên điện thoại (Quy định công ty, Hướng dẫn sử dụng).
 * Yêu cầu 2026-11-23: công nhân khó thao tác trên mobile — chữ nhỏ, đọc xong phải cuộn ngược lên.
 *
 *   <div data-doc-font></div>   → chèn 3 nút cỡ chữ A− / A / A+ (nhớ lựa chọn trên máy)
 *   <div data-doc-body>…</div>  → vùng nội dung được phóng to/thu nhỏ
 * Tự thêm nút "lên đầu trang" khi đã cuộn xuống xa (nổi trên thanh menu dưới).
 */
(function (window, document) {
    'use strict';

    var KEY = 'docFontLevel';
    var SIZES = [15, 17, 19];   // px — mức giữa là mặc định trên điện thoại

    function load() {
        try { var v = parseInt(localStorage.getItem(KEY), 10); if (v >= 0 && v < SIZES.length) return v; } catch (e) { }
        return 1;
    }
    function save(v) { try { localStorage.setItem(KEY, String(v)); } catch (e) { } }

    function apply(level) {
        document.querySelectorAll('[data-doc-body]').forEach(function (el) {
            el.style.setProperty('--doc-font', SIZES[level] + 'px');
        });
        document.querySelectorAll('.doc-font-btn').forEach(function (b) {
            b.classList.toggle('active', +b.dataset.level === level);
            b.setAttribute('aria-pressed', +b.dataset.level === level ? 'true' : 'false');
        });
    }

    function buildFontBar(host) {
        var labels = [['A−', 'Chữ nhỏ'], ['A', 'Chữ vừa'], ['A+', 'Chữ lớn']];
        host.classList.add('doc-font-bar');
        host.setAttribute('role', 'group');
        host.setAttribute('aria-label', 'Cỡ chữ');
        host.innerHTML = labels.map(function (l, i) {
            return '<button type="button" class="doc-font-btn doc-font-' + i + '" data-level="' + i +
                   '" aria-label="' + l[1] + '">' + l[0] + '</button>';
        }).join('');
        host.addEventListener('click', function (e) {
            var b = e.target.closest('.doc-font-btn');
            if (!b) return;
            var lv = +b.dataset.level;
            save(lv);
            apply(lv);
            if (window.NativeUI) window.NativeUI.haptic('light');
        });
    }

    function buildToTop() {
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'doc-totop';
        btn.setAttribute('aria-label', 'Lên đầu trang');
        btn.innerHTML = '<i class="bi bi-arrow-up"></i>';
        btn.addEventListener('click', function () { window.scrollTo({ top: 0, behavior: 'smooth' }); });
        document.body.appendChild(btn);
        var shown = false;
        window.addEventListener('scroll', function () {
            var s = window.scrollY > 700;
            if (s !== shown) { shown = s; btn.classList.toggle('show', s); }
        }, { passive: true });
    }

    // Bảng nhiều cột trên điện thoại: giữ cột đủ rộng để đọc, cho vuốt ngang + nhắc người dùng
    // (trước đây bảng bị bóp còn 1–2 chữ mỗi dòng). Chạy sau khi trang đã bọc bảng vào khung cuộn.
    function wideTables() {
        document.querySelectorAll('[data-doc-body] table').forEach(function (t) {
            var row = t.rows && t.rows[0];
            var cols = row ? Array.prototype.reduce.call(row.cells, function (n, c) { return n + (c.colSpan || 1); }, 0) : 0;
            if (cols < 3) return;
            t.classList.add('doc-wide');
            var box = t.parentElement;
            if (box && box.scrollWidth > box.clientWidth + 4 && !(box.previousElementSibling && box.previousElementSibling.classList.contains('doc-swipe-hint'))) {
                var hint = document.createElement('div');
                hint.className = 'doc-swipe-hint';
                hint.innerHTML = '<i class="bi bi-arrow-left-right"></i> Vuốt ngang để xem hết bảng';
                box.parentNode.insertBefore(hint, box);
            }
        });
    }

    function init() {
        if (!document.querySelector('[data-doc-body]')) return;
        document.querySelectorAll('[data-doc-font]').forEach(buildFontBar);
        apply(load());
        buildToTop();
        setTimeout(wideTables, 0);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
}(window, document));
