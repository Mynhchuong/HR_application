/**
 * NativeUI — thông báo / xác nhận / cảnh báo bằng giao diện GỐC của điện thoại khi chạy trong app My Samho
 * (yêu cầu 2026-11-21: khách review app thấy toast/modal kiểu web là biết ngay app chỉ là WebView).
 *
 * Không phải sửa trang nào: file này bọc lại 2 hàm dùng chung có sẵn trong layout
 *   - window.showToast(message, type)               → Android: Snackbar Material · iOS: banner trượt từ trên xuống
 *   - window.showConfirm(message, onConfirm, opts)  → hộp thoại gốc (MaterialAlertDialog / UIAlertController)
 * và nút Đăng xuất (#logoutModal) → hộp thoại gốc.
 * alert()/confirm() của JS thì app tự hiện hộp thoại gốc (WebChromeClient / WKUIDelegate), không qua đây.
 *
 * Đúng NATIVE_HELPER_RULES.md: 1 bridge generic "nativeUI" (native chỉ vẽ giao diện, không biết nghiệp vụ),
 * luôn có fallback: trình duyệt, hoặc app bản cũ chưa có bridge → giữ nguyên giao diện web như trước.
 *
 * Gọi thẳng (khi cần): NativeUI.toast(msg, type) · NativeUI.confirm({title, message, okText, cancelText,
 * destructive}, cb(ok)) · NativeUI.alert({title, message}, cb) · NativeUI.haptic('success'|'warning'|'error'|'light')
 */
(function (window, document) {
    'use strict';

    var me = document.currentScript;
    var DEF = {
        title:  (me && me.getAttribute('data-title'))  || 'Xác nhận',
        ok:     (me && me.getAttribute('data-ok'))     || 'Xóa',
        cancel: (me && me.getAttribute('data-cancel')) || 'Hủy'
    };

    function iosBridge() { return window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.nativeUI; }
    function androidBridge() { return window.Android && typeof window.Android.nativeUI === 'function'; }
    function available() { return !!(iosBridge() || androidBridge()); }

    function post(payload) {
        try {
            if (iosBridge()) { window.webkit.messageHandlers.nativeUI.postMessage(payload); return true; }
            if (androidBridge()) { window.Android.nativeUI(JSON.stringify(payload)); return true; }
        } catch (e) { /* bridge lỗi → để nơi gọi tự dùng bản web */ }
        return false;
    }

    // Kết quả từ native trả về qua NativeUI._done(id, ok)
    var pending = {}, seq = 0;
    function track(cb) { var id = 'n' + (++seq); pending[id] = cb; return id; }

    // Thông báo của web đôi khi là HTML (<b>, <br>) → native chỉ nhận chữ thuần
    function toText(html) {
        if (html == null) return '';
        var s = String(html);
        if (s.indexOf('<') < 0 && s.indexOf('&') < 0) return s.trim();
        var doc = new DOMParser().parseFromString('<body>' + s.replace(/<br\s*\/?>/gi, '\n')
            .replace(/<\/(p|div|li)>/gi, '\n'), 'text/html');
        return (doc.body.textContent || '').replace(/\n{3,}/g, '\n\n').trim();
    }

    var NativeUI = {
        available: available,
        toast: function (message, type) {
            return post({ action: 'toast', message: toText(message), type: type || 'success' });
        },
        confirm: function (o, cb) {
            o = o || {};
            var id = track(cb);
            if (!post({ action: 'confirm', id: id, title: toText(o.title || ''), message: toText(o.message || ''),
                        okText: o.okText || 'OK', cancelText: o.cancelText || DEF.cancel, destructive: !!o.destructive })) {
                delete pending[id];
                return false;
            }
            return true;
        },
        alert: function (o, cb) {
            o = o || {};
            var id = track(cb);
            if (!post({ action: 'alert', id: id, title: toText(o.title || ''), message: toText(o.message || ''),
                        okText: o.okText || 'OK' })) {
                delete pending[id];
                return false;
            }
            return true;
        },
        haptic: function (style) { return post({ action: 'haptic', style: style || 'light' }); },
        _done: function (id, ok) {
            var cb = pending[id];
            delete pending[id];
            if (typeof cb === 'function') { try { cb(!!ok); } catch (e) { console.error(e); } }
        }
    };
    window.NativeUI = NativeUI;

    if (!available()) return;   // trình duyệt / app bản cũ: giữ nguyên toàn bộ giao diện web
    document.documentElement.classList.add('native-ui');

    // ── Bọc showToast ────────────────────────────────────────────────────────────
    var webToast = window.showToast;
    window.showToast = function (message, type) {
        if (!NativeUI.toast(message, type) && typeof webToast === 'function') webToast(message, type);
    };

    // ── Bọc showConfirm ──────────────────────────────────────────────────────────
    var webConfirm = window.showConfirm;
    window.showConfirm = function (message, onConfirm, options) {
        var o = options || {};
        var ok = NativeUI.confirm({
            title: o.title || DEF.title,
            message: message,
            okText: o.okText || DEF.ok,
            cancelText: o.cancelText || DEF.cancel,
            // Nút đỏ (mặc định của showConfirm là btn-danger) → nút "huỷ diệt" kiểu hệ điều hành
            destructive: !o.okClass || /danger/.test(o.okClass)
        }, function (yes) { if (yes && typeof onConfirm === 'function') onConfirm(); });
        if (!ok && typeof webConfirm === 'function') webConfirm(message, onConfirm, options);
    };

    // ── Rung nhẹ khi bấm thanh menu dưới (như tab bar của app gốc) ───────────────
    // pointerdown thay vì click: rung đúng lúc ngón tay chạm, không chờ nhả tay / chuyển trang.
    document.addEventListener('pointerdown', function (e) {
        var t = e.target && e.target.closest && e.target.closest('.mobile-bottom-nav a, .mobile-bottom-nav button');
        if (t) NativeUI.haptic('light');
    }, { passive: true, capture: true });

    // ── Kéo xuống để tải lại (Android): chỉ cho phép khi thật sự đang ở đầu trang ─────────────────
    // App chỉ biết vị trí cuộn của CẢ trang. Chạm trong 1 khung cuộn riêng (danh sách chat, bảng, modal)
    // chưa lên tới đầu thì kéo xuống phải là cuộn khung đó, không được tải lại trang (mất dữ liệu đang nhập).
    // iOS tự xử lý đúng việc này nên bỏ qua lệnh "pull".
    function pullBlocked(el) {
        if (document.querySelector('.modal.show, .offcanvas.show')) return true;
        for (; el && el !== document.body && el !== document.documentElement; el = el.parentElement) {
            if (el.scrollTop > 0) {
                var oy = getComputedStyle(el).overflowY;
                if (oy === 'auto' || oy === 'scroll') return true;
            }
        }
        return false;
    }
    if (androidBridge()) {
        document.addEventListener('touchstart', function (e) {
            post({ action: 'pull', allow: !pullBlocked(e.target) });
        }, { passive: true, capture: true });
    }

    // ── Đăng xuất: hộp thoại gốc thay modal web ─────────────────────────────────
    // Bắt ở sự kiện "show.bs.modal" của chính modal (chặn được mọi nút mở nó, kể cả nút tự gọi
    // bootstrap.Modal bằng JS) rồi huỷ — bắt ở cú click thì modal web vẫn bật song song (thử thật 2026-11-21).
    document.addEventListener('show.bs.modal', function (e) {
        var modal = e.target;
        if (!modal || modal.id !== 'logoutModal') return;
        var form = document.getElementById('logoutForm');
        if (!form) return;
        var title = (modal.querySelector('h5') || {}).textContent || '';
        var msg = (modal.querySelector('p') || {}).textContent || '';
        var okBtn = form.querySelector('button[type="submit"]');
        var cancelBtn = modal.querySelector('[data-bs-dismiss="modal"]');
        var shown = NativeUI.confirm({
            title: title.trim(), message: msg.trim(),
            okText: okBtn ? okBtn.textContent.trim() : 'OK',
            cancelText: cancelBtn ? cancelBtn.textContent.trim() : DEF.cancel,
            destructive: true
        }, function (yes) {
            if (!yes) return;
            // requestSubmit để chạy đủ các handler submit sẵn có (huỷ token FCM, xoá thông báo...)
            if (form.requestSubmit) form.requestSubmit(); else form.submit();
        });
        if (shown) e.preventDefault();   // Bootstrap: preventDefault ở show.bs.modal = không mở modal
    });

}(window, document));
