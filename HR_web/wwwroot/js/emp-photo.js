/**
 * HrEmpPhoto — ảnh nhân viên ở các trang duyệt (yêu cầu 2026-11-21).
 *
 * Expat / quản lý duyệt cho rất nhiều người, không nhớ hết tên + mã thẻ → cần nhìn ảnh thẻ để biết là ai.
 * Bật/tắt ở Cài đặt ("Hiện ảnh nhân viên"); layout đặt <html data-ep-on="1"> khi bật
 * (mặc định: Expat bật, quản lý người Việt tắt — xem UserRoleExtensions.EmpPhotoOn).
 *
 * Gắn cho 1 trang: thêm vào ô / phần tử đang hiện mã NV
 *     data-emp-photo="${EMPCD}" data-emp-name="${EMP_NAME}"
 * rồi thôi — KHÔNG cần gọi gì thêm:
 *   - Bảng (web): tự chèn ảnh tròn đầu ô, kể cả danh sách tải AJAX sau này (MutationObserver).
 *   - Thẻ điện thoại (mobile-table.js): ảnh thẻ dọc bên trái mỗi thẻ.
 *   - Bấm ảnh → xem to (web: khung giữa màn hình; điện thoại: tấm trượt từ dưới lên, kéo xuống / nút Back để đóng).
 * Tắt ở Cài đặt → trang giữ y như cũ, không tải ảnh nào.
 *
 * Ảnh lấy từ Image/GetEmployeeThumb (đã thu nhỏ + cache, bắt buộc đăng nhập), tải lười (loading=lazy).
 */
(function (window, document) {
    'use strict';

    var root = document.documentElement;
    var THUMB = root.getAttribute('data-ep-thumb') || '';
    var TXT_NONE = root.getAttribute('data-ep-none') || 'Không có ảnh';
    var TXT_CLOSE = root.getAttribute('data-ep-close') || 'Đóng';
    var SEL = '[data-emp-photo]';

    function enabled() { return root.getAttribute('data-ep-on') === '1' && !!THUMB; }

    function url(code, big) {
        return THUMB + (THUMB.indexOf('?') >= 0 ? '&' : '?') + 'empCd=' + encodeURIComponent(code) + (big ? '&s=640' : '');
    }

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    var PERSON_SVG =
        '<svg class="ep-ph" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">' +
        '<path d="M12 12a4.5 4.5 0 1 0 0-9 4.5 4.5 0 0 0 0 9Zm0 2c-4.4 0-8 2.2-8 5v1.5c0 .3.2.5.5.5h15c.3 0 .5-.2.5-.5V19c0-2.8-3.6-5-8-5Z"/></svg>';

    /** HTML của 1 ảnh NV (nút bấm được). cls: '' (tròn trong bảng) | 'ep-av--card' (thẻ mobile). */
    function avatarHtml(code, name, cls) {
        code = String(code || '').trim();
        if (!code) return '';
        return '<button type="button" class="ep-av ' + (cls || '') + '" data-ep="' + esc(code) + '" data-ep-name="' + esc(name || '') +
            '" aria-label="' + esc(code) + '">' + PERSON_SVG +
            '<img src="' + esc(url(code)) + '" alt="" loading="lazy" decoding="async" ' +
            'onload="this.classList.add(\'ep-ok\')" onerror="this.remove()"></button>';
    }

    /** Chèn ảnh vào các phần tử data-emp-photo chưa xử lý trong vùng `scope`. */
    function decorate(scope) {
        if (!enabled()) return;
        var list = (scope || document).querySelectorAll(SEL + ':not([data-ep-done])');
        for (var i = 0; i < list.length; i++) {
            var el = list[i];
            el.setAttribute('data-ep-done', '1');
            // Thẻ mobile / hộp chi tiết của mobile-table.js là BẢN SAO nội dung ô bảng — thẻ đã có chỗ ảnh riêng
            if (el.closest && el.closest('.mt-card, #mtDetailModal')) continue;
            var code = el.getAttribute('data-emp-photo');
            if (!code) continue;
            el.insertAdjacentHTML('afterbegin', avatarHtml(code, el.getAttribute('data-emp-name'), ''));
        }
    }

    // ── Xem ảnh to ────────────────────────────────────────────────────────────────
    var lb = null, lbImg = null, lbLo = null, lbNone = null, lbName = null, lbCode = null, lbX = null;
    var openState = false, pushed = false, lastFocus = null;
    var isMobile = function () { return window.matchMedia('(max-width: 767.98px)').matches; };

    function ensureLightbox() {
        if (lb) return;
        lb = document.createElement('div');
        lb.className = 'ep-lb';
        lb.hidden = true;
        lb.innerHTML =
            '<div class="ep-lb-bg"></div>' +
            '<div class="ep-lb-card" role="dialog" aria-modal="true">' +
            '  <div class="ep-lb-grab"></div>' +
            '  <button type="button" class="ep-lb-x" aria-label="' + esc(TXT_CLOSE) + '">&times;</button>' +
            '  <div class="ep-lb-photo">' +
            '    <div class="ep-lb-none">' + PERSON_SVG.replace('ep-ph', '') + '<span>' + esc(TXT_NONE) + '</span></div>' +
            '    <img class="ep-lo" alt="">' +
            '    <img alt="">' +
            '  </div>' +
            '  <div class="ep-lb-name vni-font"></div>' +
            '  <div class="ep-lb-code"></div>' +
            '</div>';
        document.body.appendChild(lb);
        var imgs = lb.querySelectorAll('.ep-lb-photo img');
        lbLo = imgs[0]; lbImg = imgs[1];
        lbNone = lb.querySelector('.ep-lb-none');
        lbName = lb.querySelector('.ep-lb-name');
        lbCode = lb.querySelector('.ep-lb-code');
        lbX = lb.querySelector('.ep-lb-x');

        lb.querySelector('.ep-lb-bg').addEventListener('click', function () { close(); });
        lbX.addEventListener('click', function () { close(); });
        bindDrag(lb.querySelector('.ep-lb-card'));
    }

    function open(code, name) {
        if (!code) return;
        ensureLightbox();
        lastFocus = document.activeElement;

        // Ảnh nhỏ (đã có sẵn trong cache trình duyệt) hiện ngay, mờ nhẹ; ảnh nét tải xong thì thay vào.
        lbNone.style.display = 'none';
        lbLo.style.display = ''; lbImg.style.display = 'none';
        lbLo.onerror = function () { lbLo.style.display = 'none'; lbNone.style.display = ''; };
        lbLo.src = url(code);
        lbImg.onload = function () { lbImg.style.display = ''; lbLo.style.display = 'none'; };
        lbImg.onerror = function () { lbImg.style.display = 'none'; };
        lbImg.src = url(code, true);

        lbName.textContent = name || '';
        lbName.style.display = name ? '' : 'none';
        lbCode.textContent = code;

        lb.style.removeProperty('--ep-drag');
        lb.hidden = false;
        void lb.offsetWidth;            // cho trình duyệt vẽ trạng thái đầu rồi mới chạy hiệu ứng
        lb.classList.add('ep-in');
        openState = true;

        // Điện thoại: nút Back đóng ảnh thay vì rời trang
        if (isMobile() && window.history && history.pushState) {
            try { history.pushState({ epPhoto: 1 }, ''); pushed = true; } catch (e) { pushed = false; }
        }
        setTimeout(function () { try { lbX.focus({ preventScroll: true }); } catch (e) { } }, 50);
    }

    function close(fromPop) {
        if (!openState) return;
        openState = false;
        lb.classList.remove('ep-in');
        setTimeout(function () { if (!openState) { lb.hidden = true; lbImg.src = ''; } }, 330);
        if (pushed && !fromPop) { pushed = false; try { history.back(); } catch (e) { } }
        pushed = false;
        if (lastFocus && lastFocus.focus) { try { lastFocus.focus({ preventScroll: true }); } catch (e) { } }
    }

    window.addEventListener('popstate', function () { if (openState) close(true); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && openState) close(); });

    // Kéo tấm ảnh xuống để đóng (điện thoại)
    function bindDrag(card) {
        var startY = 0, dy = 0, dragging = false;
        card.addEventListener('touchstart', function (e) {
            if (!isMobile() || e.touches.length !== 1) return;
            dragging = true; startY = e.touches[0].clientY; dy = 0;
            lb.classList.add('ep-drag');
        }, { passive: true });
        card.addEventListener('touchmove', function (e) {
            if (!dragging) return;
            dy = Math.max(0, e.touches[0].clientY - startY);
            lb.style.setProperty('--ep-drag', dy + 'px');
        }, { passive: true });
        function end() {
            if (!dragging) return;
            dragging = false;
            lb.classList.remove('ep-drag');
            if (dy > 90) close();
            else lb.style.removeProperty('--ep-drag');
        }
        card.addEventListener('touchend', end);
        card.addEventListener('touchcancel', end);
    }

    // Bấm ảnh: chặn ngay ở pha capture để KHÔNG kích hoạt việc bấm dòng/thẻ (mở chi tiết) phía dưới.
    document.addEventListener('click', function (e) {
        var av = e.target && e.target.closest ? e.target.closest('.ep-av') : null;
        if (!av) return;
        e.preventDefault();
        e.stopPropagation();
        open(av.getAttribute('data-ep'), av.getAttribute('data-ep-name'));
    }, true);

    // ── Tự xử lý danh sách tải sau (AJAX, lọc, chuyển trang) ───────────────────────
    var pending = false;
    function schedule() {
        if (pending) return;
        pending = true;
        (window.requestAnimationFrame || setTimeout)(function () { pending = false; decorate(document); });
    }
    function start() {
        if (!enabled()) return;
        decorate(document);
        if (window.MutationObserver) {
            new MutationObserver(function (muts) {
                for (var i = 0; i < muts.length; i++) {
                    if (muts[i].addedNodes && muts[i].addedNodes.length) { schedule(); return; }
                }
            }).observe(document.body, { childList: true, subtree: true });
        }
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();

    window.HrEmpPhoto = { enabled: enabled, avatarHtml: avatarHtml, decorate: decorate, open: open, close: close };

}(window, document));
