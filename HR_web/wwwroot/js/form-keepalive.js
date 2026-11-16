/**
 * FormKeepAlive — giữ cho form soạn thảo LÂU không bị "Thao tác đã hết hiệu lực".
 *
 * Ca thật (phản ánh 2026-11-16): HR soạn bản tin cả tiếng rồi bấm Lưu thì bị chặn và MẤT sạch
 * bài đang gõ. Trang mở lâu mà không gọi server lần nào thì token chống giả mạo nhúng trong form
 * cũ dần, trong khi SessionValidator cứ 15 phút lại cấp lại cookie đăng nhập — hai bên lệch nhau
 * là Antiforgery từ chối.
 *
 * Cách làm: cứ 10 phút (ngắn hơn chu kỳ 15 phút kia) gọi /Account/KeepAlive để vừa giữ phiên sống
 * vừa XIN TOKEN MỚI, rồi thay vào mọi input __RequestVerificationToken trên trang. Nhờ vậy lúc
 * bấm Lưu luôn cầm token còn hạn.
 *
 * Dùng: chỉ cần nhúng file này vào trang có form soạn lâu. Không cần gọi hàm gì.
 */
(function () {
    'use strict';

    var EVERY_MS = 10 * 60 * 1000;   // 10 phút — phải NGẮN HƠN chu kỳ 15 phút của SessionValidator

    function tokenInputs() {
        return document.querySelectorAll('input[name="__RequestVerificationToken"]');
    }

    function refresh() {
        // Trang ẩn (tab nền) thì bỏ qua — đỡ gọi vô ích; quay lại tab sẽ gọi ngay (xem bên dưới).
        if (document.hidden) return;

        var base = (document.querySelector('base') || {}).href || '';
        fetch((window.KEEPALIVE_URL || '/Account/KeepAlive'), {
            method: 'GET',
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (d) {
            if (!d || !d.ok || !d.token) return;
            tokenInputs().forEach(function (i) { i.value = d.token; });
            // Nhiều trang giữ token trong 1 biến JS để gửi kèm header AJAX — cập nhật luôn.
            if (typeof window.antiForgery !== 'undefined') window.antiForgery = d.token;
        })
        .catch(function () { /* mất mạng tạm thời: lần sau gọi lại, không làm phiền người dùng */ });
    }

    if (tokenInputs().length === 0) return;   // trang không có form -> không cần

    setInterval(refresh, EVERY_MS);
    // Quay lại tab sau khi để lâu: làm mới ngay, đừng đợi hết chu kỳ rồi mới có token mới.
    document.addEventListener('visibilitychange', function () { if (!document.hidden) refresh(); });
})();
