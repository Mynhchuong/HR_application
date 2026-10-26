// Helper dùng chung cho mọi trang cần xem ảnh phóng to/full-screen — pinch-zoom, vuốt chuyển ảnh,
// vuốt xuống để đóng, nút Lưu ảnh. Chạy thuần JS (không thư viện ngoài) — hoạt động tốt trên cả trình
// duyệt desktop lẫn mobile, KHÔNG phụ thuộc app native (yêu cầu 2026-10-26: "ưu tiên tối ưu web, có
// app thì xài app" — link chạy 3 môi trường: desktop web, app, trình duyệt mobile).
//
// Nút "Lưu ảnh" dùng link <a download> bình thường — iOS/Android đã có sẵn cơ chế tải file native tốt
// (iOS: share sheet "Lưu ảnh" vào Photos; Android: DownloadManager vào Downloads, xem WKDownloadDelegate
// / MainActivity.onDownloadStart) nên không cần viết bridge riêng, tự động được native nâng cấp khi
// chạy trong app.
//
// Cách dùng:
//   <script src="~/js/media-viewer.js"></script>
//   ...
//   MediaViewer.open(['url1.jpg', 'url2.jpg'], 0);   // mảng URL ảnh trong cùng 1 "album", index bắt đầu
//   MediaViewer.open([{url:'a.jpg', type:'image'}, {url:'b.mp4', type:'video'}], 0);  // trộn ảnh + video
(function () {
    const STYLE = `
        .mv-overlay { display:none; position:fixed; inset:0; z-index:10000; background:rgba(0,0,0,.92);
            align-items:center; justify-content:center; touch-action:none; }
        .mv-overlay img.mv-img { max-width:92vw; max-height:88vh; touch-action:none; user-select:none;
            transition:transform .05s linear; will-change:transform; }
        .mv-overlay.mv-animating img.mv-img { transition:transform .2s ease-out; }
        .mv-overlay video.mv-video { max-width:92vw; max-height:88vh; }
        .mv-close, .mv-save, .mv-prev, .mv-next { position:absolute; background:rgba(0,0,0,.45);
            color:#fff; border:none; border-radius:50%; cursor:pointer; display:flex;
            align-items:center; justify-content:center; }
        .mv-close { top:16px; right:16px; width:40px; height:40px; font-size:24px; line-height:1; }
        .mv-save  { top:16px; right:66px; width:40px; height:40px; }
        .mv-save .material-symbols-rounded { font-size:20px; }
        .mv-prev, .mv-next { top:50%; width:44px; height:44px; font-size:28px; transform:translateY(-50%); }
        .mv-prev { left:12px; } .mv-next { right:12px; }
        .mv-counter { position:absolute; bottom:20px; left:50%; transform:translateX(-50%);
            color:#fff; font-size:.85rem; background:rgba(0,0,0,.45); padding:3px 12px; border-radius:999px; }
    `;

    let items = [], idx = 0, scale = 1, startDist = 0, startScale = 1;
    let panX = 0, panY = 0, dragStartX = 0, dragStartY = 0, isDragging = false, isPinching = false;
    let el, imgEl, videoEl, counterEl;

    function ensureEl() {
        if (el) return;
        const styleTag = document.createElement('style');
        styleTag.textContent = STYLE;
        document.head.appendChild(styleTag);

        el = document.createElement('div');
        el.className = 'mv-overlay';
        el.innerHTML =
            '<button class="mv-close" aria-label="Close">&times;</button>' +
            '<button class="mv-save" aria-label="Save" title="Lưu"><span class="material-symbols-rounded">download</span></button>' +
            '<button class="mv-prev" aria-label="Previous">&lsaquo;</button>' +
            '<button class="mv-next" aria-label="Next">&rsaquo;</button>' +
            '<div class="mv-counter"></div>' +
            '<img class="mv-img" alt="" />' +
            '<video class="mv-video" controls playsinline></video>';
        document.body.appendChild(el);
        imgEl = el.querySelector('.mv-img');
        videoEl = el.querySelector('.mv-video');
        counterEl = el.querySelector('.mv-counter');

        el.querySelector('.mv-close').addEventListener('click', close);
        el.querySelector('.mv-prev').addEventListener('click', () => go(-1));
        el.querySelector('.mv-next').addEventListener('click', () => go(1));
        el.querySelector('.mv-save').addEventListener('click', saveCurrent);
        el.addEventListener('click', e => { if (e.target === el) close(); });

        imgEl.addEventListener('touchstart', onTouchStart, { passive: false });
        imgEl.addEventListener('touchmove', onTouchMove, { passive: false });
        imgEl.addEventListener('touchend', onTouchEnd);
        imgEl.addEventListener('dblclick', toggleZoom);
        document.addEventListener('keydown', onKey);
    }

    function onKey(e) {
        if (!el || el.style.display !== 'flex') return;
        if (e.key === 'Escape') close();
        if (e.key === 'ArrowLeft') go(-1);
        if (e.key === 'ArrowRight') go(1);
    }

    function render() {
        const item = items[idx];
        const isVideo = item.type === 'video';

        videoEl.pause();
        videoEl.removeAttribute('src');
        videoEl.load();

        if (isVideo) {
            imgEl.style.display = 'none';
            videoEl.style.display = 'block';
            videoEl.src = item.url;
        } else {
            videoEl.style.display = 'none';
            imgEl.style.display = 'block';
            imgEl.src = item.url;
        }
        resetTransform();
        counterEl.textContent = items.length > 1 ? `${idx + 1}/${items.length}` : '';
        el.querySelector('.mv-prev').style.display = (items.length > 1 && idx > 0) ? 'flex' : 'none';
        el.querySelector('.mv-next').style.display = (items.length > 1 && idx < items.length - 1) ? 'flex' : 'none';
    }

    function resetTransform() {
        scale = 1; panX = 0; panY = 0;
        imgEl.style.transform = 'translate(0px,0px) scale(1)';
    }

    function applyTransform() {
        imgEl.style.transform = `translate(${panX}px,${panY}px) scale(${scale})`;
    }

    function toggleZoom() {
        el.classList.add('mv-animating');
        if (scale > 1) resetTransform();
        else { scale = 2.2; applyTransform(); }
        setTimeout(() => el.classList.remove('mv-animating'), 220);
    }

    function go(delta) {
        const next = idx + delta;
        if (next < 0 || next >= items.length) return;
        idx = next;
        render();
    }

    function dist(touches) {
        const dx = touches[0].clientX - touches[1].clientX;
        const dy = touches[0].clientY - touches[1].clientY;
        return Math.sqrt(dx * dx + dy * dy);
    }

    function onTouchStart(e) {
        if (e.touches.length === 2) {
            isPinching = true;
            startDist = dist(e.touches);
            startScale = scale;
        } else if (e.touches.length === 1) {
            isDragging = true;
            dragStartX = e.touches[0].clientX - panX;
            dragStartY = e.touches[0].clientY - panY;
        }
    }

    function onTouchMove(e) {
        if (e.touches.length === 2) {
            e.preventDefault();
            const d = dist(e.touches);
            scale = Math.min(Math.max(startScale * (d / startDist), 1), 4);
            applyTransform();
        } else if (e.touches.length === 1 && isDragging) {
            e.preventDefault();
            panX = e.touches[0].clientX - dragStartX;
            panY = e.touches[0].clientY - dragStartY;
            applyTransform();
        }
    }

    function onTouchEnd() {
        // Chưa zoom (scale<=1) mà vuốt dọc đủ xa -> đóng (vuốt xuống/lên để tắt, kiểu Instagram/Zalo).
        if (!isPinching && scale <= 1 && Math.abs(panY) > 110 && Math.abs(panY) > Math.abs(panX)) {
            close();
            return;
        }
        isPinching = false;
        isDragging = false;
        if (scale <= 1.05) { el.classList.add('mv-animating'); resetTransform(); setTimeout(() => el.classList.remove('mv-animating'), 200); }
    }

    function saveCurrent() {
        const a = document.createElement('a');
        a.href = items[idx].url;
        a.download = '';
        document.body.appendChild(a);
        a.click();
        a.remove();
    }

    function close() {
        if (!el) return;
        videoEl.pause();
        el.style.display = 'none';
        document.body.style.overflow = '';
    }

    window.MediaViewer = {
        // list: mảng url (ảnh, giữ tương thích cũ) HOẶC mảng {url, type:'image'|'video'}
        open: function (list, startIndex) {
            ensureEl();
            items = (list || [])
                .map(it => typeof it === 'string' ? { url: it, type: 'image' } : it)
                .filter(it => it && it.url);
            if (!items.length) return;
            idx = Math.max(0, Math.min(startIndex || 0, items.length - 1));
            render();
            el.style.display = 'flex';
            document.body.style.overflow = 'hidden';
        }
    };
})();
