/*
 * VideoFullscreen — helper dùng chung để thêm nút "Toàn màn hình" / "Thoát toàn màn hình"
 * cho bất kỳ <video> nào.
 *
 * Cách dùng:
 *   1. Tự động: thêm attribute `data-fullscreen="auto"` vào <video>
 *      <video data-fullscreen="auto" controls> ... </video>
 *      → helper tự attach khi DOMContentLoaded
 *
 *   2. Thủ công (gọi trong code):
 *      VideoFullscreen.attach(document.querySelector('#myVideo'));
 *      VideoFullscreen.attachAll('.my-section video');
 *
 *   3. Toggle bằng code:
 *      VideoFullscreen.toggle(videoEl);
 */
(function () {
    'use strict';

    const ICONS = {
        enter: '<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z"/></svg>',
            exit: '<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M5 16h3v3h2v-5H5v2zm3-8H5v2h5V5H8v3zm6 11h2v-3h3v-2h-5v5zm2-11V5h-2v5h5V8h-3z"/></svg>'
    };

    function isFullscreen() {
        return !!(document.fullscreenElement
            || document.webkitFullscreenElement
            || document.msFullscreenElement);
    }

    function requestFs(el) {
        if (el.requestFullscreen)       return el.requestFullscreen();
        if (el.webkitRequestFullscreen) return el.webkitRequestFullscreen();
        if (el.msRequestFullscreen)     return el.msRequestFullscreen();
        // iOS Safari (chỉ hỗ trợ trên video element trực tiếp)
        const v = el.tagName === 'VIDEO' ? el : el.querySelector('video');
        if (v && v.webkitEnterFullscreen) { v.webkitEnterFullscreen(); return Promise.resolve(); }
        return Promise.reject('Fullscreen không được hỗ trợ');
    }

    function exitFs() {
        if (document.exitFullscreen)       return document.exitFullscreen();
        if (document.webkitExitFullscreen) return document.webkitExitFullscreen();
        if (document.msExitFullscreen)     return document.msExitFullscreen();
        return Promise.resolve();
    }

    function makeButton(label, icon, kind) {
        const b = document.createElement('button');
        b.type = 'button';
        b.className = 'vfs-btn vfs-btn-' + kind;
        b.innerHTML = icon + '<span class="vfs-btn-label">' + label + '</span>';
        return b;
    }

    function attach(videoEl, opts) {
        if (!videoEl || videoEl.tagName !== 'VIDEO') return;
        if (videoEl.dataset.vfsAttached === '1') return;
        videoEl.dataset.vfsAttached = '1';

        opts = opts || {};
        const labelEnter = opts.labelEnter || 'Toàn màn hình';
        const labelExit  = opts.labelExit  || 'Thoát toàn màn hình';

        // Bọc video trong wrapper để nút overlay đúng vị trí
        let wrap = videoEl.parentElement;
        if (!wrap.classList.contains('vfs-wrap')) {
            wrap = document.createElement('div');
            wrap.className = 'vfs-wrap';
            videoEl.parentNode.insertBefore(wrap, videoEl);
            wrap.appendChild(videoEl);
        }

        // Tạo 2 nút
        const btnEnter = makeButton(labelEnter, ICONS.enter, 'enter');
        const btnExit  = makeButton(labelExit,  ICONS.exit,  'exit');
        btnExit.style.display = 'none';
        wrap.appendChild(btnEnter);
        wrap.appendChild(btnExit);

        const updateBtns = () => {
            const fs = isFullscreen();
            btnEnter.style.display = fs ? 'none' : '';
            btnExit.style.display  = fs ? '' : 'none';
        };

        btnEnter.addEventListener('click', () => {
            requestFs(wrap).catch(() => requestFs(videoEl)).catch(() => {});
        });
        btnExit.addEventListener('click', () => { exitFs().catch(() => {}); });

        ['fullscreenchange', 'webkitfullscreenchange', 'MSFullscreenChange']
            .forEach(ev => document.addEventListener(ev, updateBtns));
    }

    function attachAll(selector) {
        document.querySelectorAll(selector || 'video[data-fullscreen="auto"]')
            .forEach(v => attach(v));
    }

    function toggle(videoEl) {
        if (isFullscreen()) exitFs();
        else requestFs(videoEl.parentElement || videoEl).catch(() => {});
    }

    document.addEventListener('DOMContentLoaded', () => attachAll());

    window.VideoFullscreen = { attach, attachAll, toggle };
})();
