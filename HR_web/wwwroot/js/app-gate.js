/**
 * AppGate — bắt phản hồi 409 (app gate: survey chưa làm, quà chưa xác nhận, ...) từ BẤT KỲ lời gọi
 * lấy dữ liệu nào trong app (AppGateFilterBase trả 409 JSON {success:false, gate, redirect_url}
 * cho request AJAX/fetch thay vì redirect, vì hầu hết trang trong app load dữ liệu qua $.ajax HOẶC
 * fetch()) và tự điều hướng sang đúng trang xử lý gate đó.
 *
 * Payload chuẩn hoá {gate, redirect_url} cho MỌI gate (Survey/Gift/Banner sau này...) — file này
 * KHÔNG cần biết tên/URL riêng của từng gate, thêm gate mới ở backend là tự động ăn theo, không
 * phải sửa JS.
 *
 * 2 cơ chế riêng biệt vì $.ajax và fetch() là 2 API độc lập, không dùng chung 1 event:
 *  - $(document).ajaxError  → bắt các lời gọi qua jQuery $.ajax/$.get/$.post/$.getJSON.
 *  - window.fetch override  → bắt các lời gọi qua fetch() thẳng (home.js, TrainingTeach,
 *    inquiry-chat.js...). Dùng response.clone() để không "ăn" mất body gốc — code gọi fetch() ở
 *    nơi khác vẫn đọc response bình thường như chưa có gì can thiệp.
 */
(function (window) {
    'use strict';

    function checkGateBlocked(json) {
        if (json && json.gate && json.redirect_url) window.location.href = json.redirect_url;
    }

    var $ = window.jQuery;
    if ($ && $(document).ajaxError) {
        $(document).ajaxError(function (event, xhr) {
            if (xhr && xhr.status === 409) {
                try { checkGateBlocked(JSON.parse(xhr.responseText)); }
                catch (e) { /* không phải JSON, bỏ qua */ }
            }
        });
    }

    if (window.fetch) {
        var originalFetch = window.fetch;
        window.fetch = function () {
            return originalFetch.apply(this, arguments).then(function (response) {
                if (response && response.status === 409) {
                    response.clone().json().then(checkGateBlocked).catch(function () { /* không phải JSON, bỏ qua */ });
                }
                return response;
            });
        };
    }
})(window);
