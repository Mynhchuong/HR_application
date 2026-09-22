/**
 * GiftGate — bắt phản hồi 409 (gift_pending) từ BẤT KỲ lời gọi lấy dữ liệu nào trong app
 * (GiftGateFilter trả 409 JSON cho request AJAX/fetch thay vì redirect, vì hầu hết trang trong
 * app load dữ liệu qua $.ajax HOẶC fetch()) và tự điều hướng sang trang xác nhận nhận quà.
 * Điều hướng trang thật (F5, chuyển trang) đã được GiftGateFilter redirect thẳng ở server,
 * không cần xử lý ở đây.
 *
 * 2 cơ chế riêng biệt vì $.ajax và fetch() là 2 API độc lập, không dùng chung 1 event:
 *  - $(document).ajaxError  → bắt các lời gọi qua jQuery $.ajax/$.get/$.post/$.getJSON.
 *  - window.fetch override  → bắt các lời gọi qua fetch() thẳng (home.js, TrainingTeach,
 *    inquiry-chat.js... review 2026-09-22 phát hiện thiếu, GiftGateFilter trả đúng 409 nhưng
 *    không ai đọc để tự điều hướng). Dùng response.clone() để không "ăn" mất body gốc — code
 *    gọi fetch() ở nơi khác vẫn đọc response bình thường như chưa có gì can thiệp.
 */
(function (window) {
    'use strict';

    function goToMyGifts() {
        window.location.href = window.GIFT_MY_GIFTS_URL || '/Gift/MyGifts';
    }

    function checkGiftPending(json) {
        if (json && json.gift_pending) goToMyGifts();
    }

    var $ = window.jQuery;
    if ($ && $(document).ajaxError) {
        $(document).ajaxError(function (event, xhr) {
            if (xhr && xhr.status === 409) {
                try { checkGiftPending(JSON.parse(xhr.responseText)); }
                catch (e) { /* không phải JSON, bỏ qua */ }
            }
        });
    }

    if (window.fetch) {
        var originalFetch = window.fetch;
        window.fetch = function () {
            return originalFetch.apply(this, arguments).then(function (response) {
                if (response && response.status === 409) {
                    response.clone().json().then(checkGiftPending).catch(function () { /* không phải JSON, bỏ qua */ });
                }
                return response;
            });
        };
    }
})(window);
