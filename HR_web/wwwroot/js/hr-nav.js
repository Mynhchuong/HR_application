/* ================================================================
   hr-nav.js — JS cho menu điều hướng (đóng/mở sidebar, tìm kiếm trong
   sidenav, ghim mục yêu thích). Trước đây nằm rải rác inline trong
   _Layout.cshtml và _SideNav.cshtml — tách ra đây cho dễ sửa (yêu cầu
   2026-10-13: menu không thân thiện, nhất là mobile, khó sửa vì
   CSS/JS lẫn trong Razor).
   ================================================================ */

(function () {
    'use strict';

    /* Nhãn hiển thị — mặc định tiếng Việt. Layout NV nước ngoài (_LayoutExpat.cshtml) đặt
       window.UI_LANG = 'en'|'ko' trước khi file này chạy (yêu cầu 2026-11-11). */
    var NAV_LBL = (function () {
        var ko = window.UI_LANG === 'ko', en = window.UI_LANG === 'en';
        return {
            pin    : ko ? '즐겨찾기에 추가' : en ? 'Pin to favourites' : 'Ghim vào Yêu thích',
            unpin  : ko ? '즐겨찾기 해제'   : en ? 'Unpin'             : 'Bỏ ghim',
            favs   : ko ? '즐겨찾기'        : en ? 'Favourites'        : 'Yêu thích'
        };
    })();


    // ───────────────────── Đóng/mở sidebar (desktop + mobile) ─────────────────────
    var body, btnToggle, btnCloseSidebar, backdrop;

    function isMobile() { return window.innerWidth <= 991.98; }

    function setMobileSidebar(isOpen) {
        body.classList.toggle("hr-mobile-open", isOpen);
        if (btnToggle) btnToggle.setAttribute("aria-expanded", isOpen ? "true" : "false");
    }

    function toggleSidebar() {
        if (isMobile()) {
            setMobileSidebar(!body.classList.contains("hr-mobile-open"));
        } else {
            body.classList.toggle("hr-menu-hidden");
            localStorage.setItem("hr_menu_hidden", body.classList.contains("hr-menu-hidden"));
        }
    }
    // Để nút "Menu" ở mobile-bottom-nav (và bất kỳ nút nào khác sau này) gọi được.
    window.hrToggleSidebar = toggleSidebar;

    function syncDesktopState() {
        if (!isMobile()) {
            setMobileSidebar(false);
            if (localStorage.getItem("hr_menu_hidden") === "true") {
                body.classList.add("hr-menu-hidden");
            } else {
                body.classList.remove("hr-menu-hidden");
            }
        }
    }

    function initSidebarToggle() {
        body = document.body;
        btnToggle = document.getElementById("hr-btnToggle");
        btnCloseSidebar = document.getElementById("hr-iconSidenav");
        backdrop = document.querySelector(".hr-backdrop");

        syncDesktopState();

        if (btnToggle) {
            btnToggle.addEventListener("click", function (e) {
                e.preventDefault();
                e.stopPropagation();
                toggleSidebar();
            });
        }
        if (btnCloseSidebar) {
            btnCloseSidebar.addEventListener("click", function (e) {
                e.preventDefault();
                e.stopPropagation();
                setMobileSidebar(false);
            });
        }
        if (backdrop) {
            backdrop.addEventListener("click", function () { setMobileSidebar(false); });
        }
        window.addEventListener("resize", function () {
            if (!isMobile()) syncDesktopState();
            else body.classList.remove("hr-menu-hidden");
        });
    }

    // ───────────────────── Ghim mục yêu thích (localStorage) ─────────────────────
    var PIN_KEY = 'hr_menu_pins';
    var PIN_MAX = 8;

    function getPins() {
        try { return JSON.parse(localStorage.getItem(PIN_KEY) || '[]'); } catch (e) { return []; }
    }
    function savePins(pins) {
        try { localStorage.setItem(PIN_KEY, JSON.stringify(pins)); } catch (e) { /* storage đầy/bị chặn — bỏ qua, không chặn thao tác chính */ }
    }
    function togglePin(data) {
        if (!data.url) return;
        var pins = getPins();
        var idx = pins.findIndex(function (p) { return p.url === data.url; });
        if (idx >= 0) {
            pins.splice(idx, 1);
        } else {
            pins.unshift(data);
            if (pins.length > PIN_MAX) pins = pins.slice(0, PIN_MAX);
        }
        savePins(pins);
        renderFavorites();
        syncPinButtons();
    }

    function syncPinButtons() {
        var pins = getPins();
        document.querySelectorAll('.menu-pin-btn[data-pin-url]').forEach(function (btn) {
            var url = btn.getAttribute('data-pin-url');
            var pinned = pins.some(function (p) { return p.url === url; });
            btn.classList.toggle('pinned', pinned);
            btn.title = pinned ? NAV_LBL.unpin : NAV_LBL.pin;
            var icon = btn.querySelector('.material-symbols-rounded');
            if (icon) icon.textContent = pinned ? 'star' : 'star_outline';
        });
    }

    function escHtml(s) {
        return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // Khối "Yêu thích" ở đầu sidenav — rỗng thì tự ẩn, không tốn chỗ. Đây là lối tắt chính giúp
    // menu không bị "phình dài hơn" trong mắt NV dù sau này có thêm bao nhiêu role/chức năng.
    function renderFavorites() {
        var wrap = document.getElementById('sidenavFavorites');
        if (!wrap) return;
        var pins = getPins();
        if (!pins.length) { wrap.classList.add('d-none'); wrap.innerHTML = ''; return; }
        wrap.classList.remove('d-none');

        var html = '<div class="menu-group-label" style="color:#f59e0b;">' +
                        '<span class="menu-group-dot" style="background:#f59e0b;"></span>' + NAV_LBL.favs +
                    '</div><ul class="navbar-nav">';
        pins.forEach(function (p) {
            html += '<li class="nav-item menu-child-item" data-search="' + escHtml((p.title || '').toLowerCase()) + '">' +
                        '<div class="d-flex align-items-center">' +
                            '<a class="nav-link text-dark rounded px-2 py-1 flex-grow-1" href="' + escHtml(p.url) + '">' +
                                '<span class="menu-child-icon">' + (window.MENU_IC3D && window.MENU_IC3D[p.icon] && window.ic3d
                                    ? window.ic3d(window.MENU_IC3D[p.icon])
                                    : '<i class="material-symbols-rounded">' + escHtml(p.icon || 'star') + '</i>') + '</span>' +
                                '<span class="nav-link-text">' + escHtml(p.title) + '</span>' +
                            '</a>' +
                            '<button type="button" class="menu-pin-btn pinned" data-pin-url="' + escHtml(p.url) + '" data-pin-title="' + escHtml(p.title) + '" data-pin-icon="' + escHtml(p.icon || '') + '" title="' + NAV_LBL.unpin + '">' +
                                '<i class="material-symbols-rounded">star</i>' +
                            '</button>' +
                        '</div>' +
                    '</li>';
        });
        html += '</ul>';
        wrap.innerHTML = html;
    }

    function initPins() {
        renderFavorites();
        syncPinButtons();
        document.addEventListener('click', function (e) {
            var btn = e.target.closest && e.target.closest('.menu-pin-btn');
            if (!btn) return;
            e.preventDefault();
            e.stopPropagation();
            togglePin({
                url: btn.getAttribute('data-pin-url'),
                title: btn.getAttribute('data-pin-title'),
                icon: btn.getAttribute('data-pin-icon')
            });
        });
    }

    // ───────────────────── Tìm kiếm trong sidenav (lọc theo tên) ─────────────────────
    function initSidenavSearch() {
        var input = document.getElementById('sidenavSearch');
        if (!input) return;

        function updateGroupLabels(sectionEl) {
            var listEl = sectionEl.querySelector('.collapse > ul, #sidenavFavorites > ul');
            if (!listEl) return;
            var lastLabel = null, anyVisible = false;
            Array.prototype.forEach.call(listEl.children, function (li) {
                if (li.classList.contains('menu-group-label')) {
                    if (lastLabel) lastLabel.style.display = anyVisible ? '' : 'none';
                    lastLabel = li;
                    anyVisible = false;
                } else if (li.classList.contains('menu-child-item') && li.style.display !== 'none') {
                    anyVisible = true;
                }
            });
            if (lastLabel) lastLabel.style.display = anyVisible ? '' : 'none';
        }

        input.addEventListener('input', function () {
            var q = this.value.trim().toLowerCase();
            var groups = document.querySelectorAll('#sidenav-collapse-main > ul > .nav-item');

            groups.forEach(function (group) {
                var collapseEl = group.querySelector('.collapse');
                var children = group.querySelectorAll('.menu-child-item');
                if (!children.length) return;

                if (q === '') {
                    children.forEach(function (c) { c.style.display = ''; });
                    group.querySelectorAll('.menu-group-label').forEach(function (l) { l.style.display = ''; });
                    group.style.display = '';
                    if (collapseEl && !collapseEl.classList.contains('show')) {
                        collapseEl.style.display = '';
                    }
                    return;
                }

                var matched = 0;
                children.forEach(function (c) {
                    var title = c.getAttribute('data-search') || '';
                    if (title.includes(q)) { c.style.display = ''; matched++; }
                    else { c.style.display = 'none'; }
                });
                updateGroupLabels(group);

                if (matched > 0) {
                    group.style.display = '';
                    if (collapseEl) collapseEl.style.display = 'block';
                } else {
                    group.style.display = 'none';
                }
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initSidebarToggle();
        initPins();
        initSidenavSearch();
    });
})();

// Bàn phím ảo trong WebView (iOS + Android): đóng bàn phím xong WebView đôi khi không tính lại vị
// trí phần tử position:fixed -> thanh menu dưới kẹt lơ lửng giữa màn hình (báo lỗi 2026-11-18).
// Bàn phím mở: ẩn thanh menu. Bàn phím đóng: cuộn 1px rồi trả lại để ép tính lại bố cục.
(function () {
    var vv = window.visualViewport;
    if (!vv) return;
    var fullH = vv.height;

    function relayout() {
        window.scrollBy(0, 1);
        window.scrollBy(0, -1);
    }

    vv.addEventListener('resize', function () {
        if (vv.height > fullH) fullH = vv.height;
        var open = vv.height < fullH - 150;
        var wasOpen = document.body.classList.contains('kb-open');
        document.body.classList.toggle('kb-open', open);
        if (wasOpen && !open) setTimeout(relayout, 60);
    });

    window.addEventListener('orientationchange', function () {
        setTimeout(function () { fullH = vv.height; }, 400);
    });

    document.addEventListener('focusout', function (e) {
        if (e.target && e.target.matches && e.target.matches('input, textarea, select, [contenteditable="true"]')) {
            setTimeout(relayout, 120);
        }
    });
})();

// Đổi icon nét mảnh (Material Symbols / Bootstrap Icons) mang ý nghĩa nội dung sang icon 3D trong vùng
// nội dung trang. Icon thao tác (mũi tên, đóng, tìm, thêm, xoá, sửa...) không có trong bảng nên giữ nguyên.
(function () {
    if (!window.IC3D_BASE) return;
    var MS = {
        warning: 'warning', priority_high: 'warning', check_circle: 'success', cancel: 'error', error: 'error', block: 'no-permission',
        badge: 'cccd-scan', contact_page: 'cccd-scan', description: 'document-request', forum: 'message', add_comment: 'direct-chat',
        rate_review: 'direct-chat', door_front: 'gatepass', draw: 'sign-needed', signature: 'sign-needed', work: 'hr-inquiry',
        work_history: 'hr-inquiry', image: 'm-image', category: 'm-topic', calendar_today: 'hr-sunday', calendar_month: 'hr-sunday',
        event: 'calendar-event', event_busy: 'leave', event_available: 'approve-leave', history: 'm-history', history_toggle_off: 'loading',
        construction: 'tpm-report', engineering: 'safety', star: 'star', stars: 'star', pin: 'pin', location_on: 'pin', mail: 'inbox',
        lock: 'password', lock_reset: 'password', lock_clock: 'meal-locked', lightbulb: 'kaizen', tips_and_updates: 'kaizen',
        verified_user: 'insurance', smart_toy: 'ai-csr', edit_note: 'survey', notification_important: 'bell', gavel: 'm-policy',
        menu_book: 'policy', system_update: 'app-update', qr_code_scanner: 'qr-scan', person_search: 'search-empty', send: 'hr-send',
        call: 'm-phone', view_column: 'm-topic', update: 'm-history', info: 'm-info'
    };
    var BI = {
        'hourglass-split': 'loading', 'calendar3': 'hr-sunday', 'calendar3-range': 'hr-sunday', 'calendar-day': 'hr-sunday',
        'check-circle': 'success', 'check-circle-fill': 'success', 'check2-circle': 'success',
        'exclamation-triangle': 'warning', 'exclamation-triangle-fill': 'warning', 'exclamation-circle': 'warning',
        'x-circle': 'error', 'x-circle-fill': 'error', 'file-earmark-excel': 'hr-excel', 'clock-history': 'm-history',
        'clock': 'shift-day', 'clock-fill': 'shift-day', 'alarm': 'overtime', 'door-open': 'gatepass',
        'calendar-check': 'approve-leave', 'calendar-x': 'leave', 'calendar2-x': 'leave', 'calendar-event': 'calendar-event',
        'calendar-heart': 'calendar-event', 'calendar-week': 'team-calendar', 'calendar2-week': 'team-calendar',
        'calendar2-plus': 'assigned-leave', 'calendar-plus': 'assigned-leave', 'chat-left-text': 'direct-chat',
        'chat-square-text': 'direct-chat', 'chat-dots': 'message', 'chat': 'message', 'people-fill': 'team', 'people': 'team',
        'person-fill': 'profile', 'person': 'profile', 'person-check': 'profile', 'person-x': 'no-permission',
        'person-fill-lock': 'password', 'lock-fill': 'password', 'lock-clock': 'meal-locked', 'person-badge': 'cccd-scan',
        'shield-check': 'insurance', 'send': 'hr-send', 'send-fill': 'hr-send', 'send-check': 'hr-send', 'bell': 'bell',
        'bell-fill': 'bell', 'journal-text': 'policy', 'book': 'policy', 'book-half': 'policy', 'list-ul': 'ot-list',
        'mortarboard': 'training', 'egg-fried': 'meal-main', 'folder2-open': 'empty', 'folder2': 'empty', 'box-seam': 'empty',
        'star': 'star', 'star-fill': 'star', 'list-check': 'survey', 'clipboard-check': 'survey', 'check2-square': 'survey',
        'patch-check': 'certificate', 'patch-check-fill': 'certificate', 'award': 'trophy', 'building': 'hr-inquiry',
        'qr-code-scan': 'qr-scan', 'geo-alt': 'pin', 'pin-angle-fill': 'pin', 'question-circle': 'guide', 'emoji-smile': 'rate-happy',
        'cake2': 'birthday', 'clipboard-data': 'hr-report', 'moon-stars': 'shift-night', 'gift': 'gift', 'gift-fill': 'gift',
        'file-earmark-text': 'document-request', 'megaphone-fill': 'bulletin-comment', 'inbox': 'inbox', 'wifi-off': 'offline',
        'image': 'm-image', 'info-circle': 'm-info', 'info-circle-fill': 'm-info', 'diagram-3': 'team', 'play-circle': 'm-play',
        'play-circle-fill': 'm-play', 'camera-video': 'm-video', 'camera-video-fill': 'm-video', 'printer': 'm-printer'
    };
    var FA = { 'users': 'team', 'circle-info': 'm-info', 'info-circle': 'm-info', 'plus-circle': 'm-plus', 'minus-circle': 'm-minus' };
    var SKIP = '#hr-sidebar, .mobile-bottom-nav, .main-header, .hdr-noti, .menu-pin-btn, .no-ic3d';

    function img(key) {
        var el = document.createElement('img');
        el.className = 'ic3d-sw';
        el.src = window.IC3D_BASE + key + (window.IC3D_EXT || '.webp');
        el.loading = 'lazy';
        el.alt = '';
        el.decoding = 'async';
        return el;
    }
    function swap(el) {
        var old = el.querySelector('img.ic3d-sw');
        // JS của trang ghi đè className (mất bi3d/fa3d) -> gỡ ảnh cũ để đổi lại theo icon mới
        if (old && el.tagName === 'I' && !el.classList.contains('bi3d') && !el.classList.contains('fa3d')) { old.remove(); old = null; }
        if (el.closest(SKIP) || old) return;
        if (el.classList.contains('material-symbols-rounded')) {
            var name = (el.textContent || '').trim();
            if (!MS[name] || el.querySelector('img')) return;
            el.textContent = '';
            el.appendChild(img(MS[name]));
            return;
        }
        if (el.tagName === 'I' && /\bfa-/.test(el.className)) {
            for (var k = 0; k < el.classList.length; k++) {
                var f = el.classList[k];
                if (f.indexOf('fa-') === 0 && FA[f.slice(3)]) {
                    el.classList.add('fa3d');
                    el.appendChild(img(FA[f.slice(3)]));
                    return;
                }
            }
            return;
        }
        if (el.classList.contains('bi')) {
            for (var i = 0; i < el.classList.length; i++) {
                var c = el.classList[i];
                if (c.indexOf('bi-') === 0 && BI[c.slice(3)]) {
                    el.classList.add('bi3d');
                    el.appendChild(img(BI[c.slice(3)]));
                    return;
                }
            }
        }
    }
    function scan(root) {
        if (!root || root.nodeType !== 1) return;
        if (root.matches && root.matches('.material-symbols-rounded, i.bi, i[class*="fa-"]')) swap(root);
        var list = root.querySelectorAll('.material-symbols-rounded, i.bi, i[class*="fa-"]');
        for (var i = 0; i < list.length; i++) swap(list[i]);
    }

    var area = document.querySelector('.main-content') || document.body;
    var pending = [], queued = false;
    function flush() {
        queued = false;
        var batch = pending; pending = [];
        for (var i = 0; i < batch.length; i++) scan(batch[i]);
    }
    function start() {
        scan(area);
        new MutationObserver(function (muts) {
            for (var i = 0; i < muts.length; i++) {
                var t = muts[i].target;
                if (muts[i].type === 'characterData') t = t.parentElement;
                if (muts[i].type === 'attributes' && !(t.tagName === 'I' && t.querySelector('img.ic3d-sw'))) continue;
                if (t) pending.push(t);
                var added = muts[i].addedNodes;
                for (var j = 0; j < added.length; j++) if (added[j].nodeType === 1) pending.push(added[j]);
            }
            if (!queued && pending.length) { queued = true; setTimeout(flush, 16); }
        }).observe(area, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class'] });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
})();
