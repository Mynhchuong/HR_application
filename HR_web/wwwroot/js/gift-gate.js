/**
 * GiftGate — bắt phản hồi 409 (gift_pending) từ BẤT KỲ lời gọi $.ajax nào trong app
 * (GiftGateFilter trả 409 JSON cho request AJAX thay vì redirect, vì hầu hết trang trong app
 * load dữ liệu qua $.ajax) và tự điều hướng sang trang xác nhận nhận quà.
 * Điều hướng trang thật (F5, chuyển trang) đã được GiftGateFilter redirect thẳng ở server,
 * không cần xử lý ở đây.
 */
(function ($) {
    'use strict';
    if (!$ || !$(document).ajaxError) return;

    $(document).ajaxError(function (event, xhr) {
        if (xhr && xhr.status === 409) {
            try {
                var res = JSON.parse(xhr.responseText);
                if (res && res.gift_pending) {
                    window.location.href = '/Gift/MyGifts';
                }
            } catch (e) { /* không phải JSON, bỏ qua */ }
        }
    });
})(window.jQuery);
