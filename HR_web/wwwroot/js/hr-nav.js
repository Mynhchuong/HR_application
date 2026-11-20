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
            favs   : ko ? '즐겨찾기'        : en ? 'Favourites'        : 'Yêu thích',
            freq   : ko ? '자주 사용'       : en ? 'Frequently used'   : 'Hay dùng',
            menu   : ko ? '메뉴'            : en ? 'Menu'              : 'Tất cả chức năng',
            search : ko ? '기능 검색...'    : en ? 'Search features...' : 'Tìm chức năng (không cần dấu)...',
            none   : ko ? '찾을 수 없습니다' : en ? 'Nothing found'     : 'Không tìm thấy chức năng nào'
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

        // Vuốt sang TRÁI trên menu để đóng (2026-11-19) — thêm 1 cách đóng tự nhiên trên điện thoại,
        // ngoài nút ✕ và chạm vùng tối. Chỉ tính khi vuốt ngang rõ ràng, vuốt dọc vẫn là cuộn menu.
        var sidebarEl = document.getElementById("hr-sidebar");
        if (sidebarEl) {
            var sx = 0, sy = 0, tracking = false;
            sidebarEl.addEventListener("touchstart", function (e) {
                if (!isMobile() || !body.classList.contains("hr-mobile-open") || e.touches.length !== 1) { tracking = false; return; }
                sx = e.touches[0].clientX; sy = e.touches[0].clientY; tracking = true;
            }, { passive: true });
            sidebarEl.addEventListener("touchend", function (e) {
                if (!tracking) return;
                tracking = false;
                var t = e.changedTouches[0];
                var dx = t.clientX - sx, dy = t.clientY - sy;
                if (dx < -70 && Math.abs(dy) < Math.abs(dx) * 0.6) setMobileSidebar(false);
            }, { passive: true });
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

    // Bỏ dấu tiếng Việt để tìm "phep" ra "Nghỉ phép" (2026-11-19 — công nhân hay gõ không dấu)
    function fold(s) {
        return String(s || '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd');
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
            var q = fold(this.value.trim());
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
                    var title = fold(c.getAttribute('data-search') || '');
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


    // ───────────────────── (2) "Hay dùng" — tự nhớ chức năng mở nhiều nhất ─────────────────────
    // Đếm mỗi lần MỞ TRANG có trong menu (không cần bấm ⭐). Lưu trên máy (localStorage), tối đa 30 mục.
    var USE_KEY = 'hr_menu_usage';
    function getUsage() { try { return JSON.parse(localStorage.getItem(USE_KEY) || '{}'); } catch (e) { return {}; } }
    function menuEntries() {
        // Mỗi mục menu có nút ghim mang sẵn url/title/icon — dùng làm nguồn dữ liệu chung.
        var seen = {}, list = [];
        document.querySelectorAll('#sidenav-collapse-main .menu-pin-btn[data-pin-url]').forEach(function (b) {
            var url = b.getAttribute('data-pin-url');
            if (!url || seen[url]) return;
            seen[url] = 1;
            list.push({ url: url, title: b.getAttribute('data-pin-title') || '', icon: b.getAttribute('data-pin-icon') || '' });
        });
        return list;
    }
    function trackUsage() {
        var path = location.pathname.toLowerCase().replace(/\/+$/, '');
        var best = null;
        menuEntries().forEach(function (m) {
            var u = m.url.toLowerCase().replace(/\/+$/, '');
            if (u && (path === u || path.indexOf(u + '/') === 0) && (!best || u.length > best.url.length)) best = m;
        });
        if (!best) return;
        var usage = getUsage();
        var cur = usage[best.url] || { n: 0 };
        usage[best.url] = { t: best.title, i: best.icon, n: cur.n + 1, l: Date.now() };
        var keys = Object.keys(usage);
        if (keys.length > 30) {
            keys.sort(function (a, b) { return (usage[a].l || 0) - (usage[b].l || 0); });
            keys.slice(0, keys.length - 30).forEach(function (k) { delete usage[k]; });
        }
        try { localStorage.setItem(USE_KEY, JSON.stringify(usage)); } catch (e) { }
    }
    function topUsed(n) {
        var usage = getUsage();
        var valid = {};
        menuEntries().forEach(function (m) { valid[m.url] = m; });   // chỉ mục còn trong menu (đổi role/xoá mục thì bỏ)
        return Object.keys(usage).filter(function (u) { return valid[u]; })
            .sort(function (a, b) { return (usage[b].n - usage[a].n) || ((usage[b].l || 0) - (usage[a].l || 0)); })
            .slice(0, n).map(function (u) { return valid[u]; });
    }
    function iconHtml(icon) {
        return window.MENU_IC3D && window.MENU_IC3D[icon] && window.ic3d
            ? window.ic3d(window.MENU_IC3D[icon])
            : '<i class="material-symbols-rounded no-ic3d">' + escHtml(icon || 'apps') + '</i>';
    }
    function tilesHtml(items) {
        return items.map(function (m) {
            return '<a class="hr-tile" href="' + escHtml(m.url) + '"><span class="hr-tile-ic">' + iconHtml(m.icon) +
                   '</span><span class="hr-tile-tx">' + escHtml(m.title) + '</span></a>';
        }).join('');
    }
    function renderFrequent() {
        var fav = document.getElementById('sidenavFavorites');
        if (!fav) return;
        var box = document.getElementById('sidenavFrequent');
        if (!box) {
            box = document.createElement('div');
            box.id = 'sidenavFrequent';
            fav.parentNode.insertBefore(box, fav);
        }
        var top = topUsed(4);
        if (top.length < 2) { box.style.display = 'none'; return; }   // mới dùng 0-1 trang thì chưa cần
        box.style.display = '';
        box.innerHTML = '<div class="hr-freq-head">' + NAV_LBL.freq + '</div><div class="hr-tiles hr-tiles-4">' + tilesHtml(top) + '</div>';
    }

    // ───────────────────── (3) Nhớ nhóm menu đang mở ─────────────────────
    var OPEN_KEY = 'hr_menu_open';
    function initRememberGroups() {
        var root = document.getElementById('sidenav-collapse-main');
        if (!root) return;
        var saved = [];
        try { saved = JSON.parse(localStorage.getItem(OPEN_KEY) || '[]'); } catch (e) { }
        saved.forEach(function (id) {
            var el = document.getElementById(id);
            if (!el || !root.contains(el) || el.classList.contains('show')) return;
            el.classList.add('show');
            var a = root.querySelector('[href="#' + id + '"]');
            if (a) a.setAttribute('aria-expanded', 'true');
        });
        function save() {
            var ids = [];
            root.querySelectorAll('.collapse.show[id]').forEach(function (c) { ids.push(c.id); });
            try { localStorage.setItem(OPEN_KEY, JSON.stringify(ids)); } catch (e) { }
        }
        root.addEventListener('shown.bs.collapse', save);
        root.addEventListener('hidden.bs.collapse', save);
    }

    // ───────────────────── (4) Chấm đỏ trên tiêu đề nhóm khi mục con có số chưa xử lý ─────────────────────
    function updateGroupBadges() {
        document.querySelectorAll('#sidenav-collapse-main > ul > .nav-item').forEach(function (g) {
            var head = g.querySelector(':scope > .nav-link');
            if (!head) return;
            var total = 0;
            g.querySelectorAll('.menu-unread-badge').forEach(function (b) {
                if (b.style.display !== 'none') total += parseInt(b.textContent, 10) || 1;
            });
            var dot = head.querySelector('.menu-group-badge');
            if (!dot) {
                dot = document.createElement('span');
                dot.className = 'menu-group-badge';
                var chev = head.querySelector(':scope > .material-symbols-rounded, :scope > i');
                head.insertBefore(dot, chev);
            }
            dot.textContent = total > 99 ? '99+' : total;
            dot.style.display = total > 0 ? '' : 'none';
        });
    }
    function initGroupBadges() {
        var root = document.getElementById('sidenav-collapse-main');
        if (!root) return;
        updateGroupBadges();
        var t;
        new MutationObserver(function () { clearTimeout(t); t = setTimeout(updateGroupBadges, 50); })
            .observe(root, { subtree: true, attributes: true, attributeFilter: ['style'], childList: true, characterData: true });
    }

    // ───────────────────── (6) Nút "Menu" ở thanh dưới: lưới ô kiểu màn hình điện thoại ─────────────────────
    // Dựng từ chính menu trái (không khai báo lại danh sách) → thêm/bớt chức năng ở SideMenuBuilder là tự có.
    function buildAppGrid() {
        var sheet = document.getElementById('hrAppGrid');
        if (sheet) return sheet;
        sheet = document.createElement('div');
        sheet.id = 'hrAppGrid';
        sheet.className = 'hr-appgrid';
        var groupsHtml = '';
        document.querySelectorAll('#sidenav-collapse-main > ul > .nav-item').forEach(function (g) {
            var title = (g.querySelector(':scope > .nav-link .nav-link-text') || {}).textContent || '';
            var items = [];
            g.querySelectorAll('.menu-child-item').forEach(function (li) {
                var a = li.querySelector('a.nav-link');
                if (!a) return;
                var badge = li.querySelector('.menu-unread-badge');
                items.push('<a class="hr-tile" href="' + escHtml(a.getAttribute('href')) + '" data-s="' + escHtml(fold(li.getAttribute('data-search') || '')) + '">' +
                    '<span class="hr-tile-ic">' + ((li.querySelector('.menu-child-icon') || {}).innerHTML || '') +
                    (badge && badge.style.display !== 'none' ? '<span class="hr-tile-badge">' + escHtml(badge.textContent) + '</span>' : '') +
                    '</span><span class="hr-tile-tx">' + escHtml(((li.querySelector('.nav-link-text') || {}).textContent || '').trim()) + '</span></a>');
            });
            if (items.length) groupsHtml += '<section class="hr-ag-group"><h6>' + escHtml(title.trim()) + '</h6><div class="hr-tiles">' + items.join('') + '</div></section>';
        });
        var top = topUsed(4);
        sheet.innerHTML =
            '<div class="hr-ag-panel" role="dialog" aria-modal="true">' +
                '<div class="hr-ag-head"><b>' + NAV_LBL.menu + '</b>' +
                    '<button type="button" class="hr-ag-close" aria-label="Close"><i class="material-symbols-rounded no-ic3d">close</i></button></div>' +
                '<div class="hr-ag-search"><i class="material-symbols-rounded no-ic3d">search</i><input type="text" placeholder="' + escHtml(NAV_LBL.search) + '" autocomplete="off"></div>' +
                '<div class="hr-ag-body">' +
                    (top.length >= 2 ? '<section class="hr-ag-group hr-ag-freq"><h6>⭐ ' + NAV_LBL.freq + '</h6><div class="hr-tiles">' + tilesHtml(top) + '</div></section>' : '') +
                    groupsHtml +
                    '<div class="hr-ag-none" style="display:none">' + NAV_LBL.none + '</div>' +
                '</div>' +
            '</div>';
        document.body.appendChild(sheet);
        sheet.addEventListener('click', function (e) { if (e.target === sheet || e.target.closest('.hr-ag-close')) closeAppGrid(); });
        var input = sheet.querySelector('.hr-ag-search input');
        input.addEventListener('input', function () {
            var q = fold(this.value.trim()), any = false;
            sheet.querySelectorAll('.hr-ag-freq').forEach(function (f) { f.style.display = q ? 'none' : ''; });
            sheet.querySelectorAll('.hr-ag-group:not(.hr-ag-freq)').forEach(function (sec) {
                var n = 0;
                sec.querySelectorAll('.hr-tile').forEach(function (t) {
                    var ok = !q || (t.getAttribute('data-s') || '').indexOf(q) >= 0;
                    t.style.display = ok ? '' : 'none';
                    if (ok) n++;
                });
                sec.style.display = n ? '' : 'none';
                if (n) any = true;
            });
            sheet.querySelector('.hr-ag-none').style.display = any ? 'none' : '';
        });
        return sheet;
    }
    function openAppGrid() {
        if (!document.querySelector('#sidenav-collapse-main .menu-child-item')) { toggleSidebar(); return; }   // không có menu → dùng menu trái
        var old = document.getElementById('hrAppGrid');
        if (old) old.remove();                     // dựng lại mỗi lần để số đỏ/Hay dùng luôn mới
        var sheet = buildAppGrid();
        requestAnimationFrame(function () { sheet.classList.add('open'); });
        body.classList.add('hr-ag-open');
    }
    function closeAppGrid() {
        var sheet = document.getElementById('hrAppGrid');
        if (!sheet) return;
        sheet.classList.remove('open');
        body.classList.remove('hr-ag-open');
        setTimeout(function () { if (!sheet.classList.contains('open')) sheet.remove(); }, 250);
    }
    window.hrOpenAppGrid = openAppGrid;

    // ───────────────────── Tìm chức năng nhanh trên web (Ctrl/⌘+K) — 2026-11-20 ─────────────────────
    // Menu nhiều mục quá, mở từng nhóm tìm không nổi. Gõ vài chữ (không dấu) → ra ngay, ↑↓ chọn, Enter mở.
    // Dữ liệu lấy từ chính menu trái (nhóm + cụm + tên trang) → thêm chức năng mới là tự có.
    var PAL_LBL = (function () {
        var ko = window.UI_LANG === 'ko', en = window.UI_LANG === 'en';
        return {
            btn  : ko ? '기능 검색' : en ? 'Search features' : 'Tìm chức năng',
            ph   : ko ? '찾을 기능을 입력하세요...' : en ? 'Type a feature name...' : 'Gõ tên chức năng (không cần dấu)...',
            freq : NAV_LBL.freq, none: NAV_LBL.none,
            hint : ko ? '↑↓ 선택 · Enter 열기 · Esc 닫기' : en ? '↑↓ select · Enter open · Esc close' : '↑↓ chọn · Enter mở · Esc đóng'
        };
    })();
    function paletteItems() {
        var list = [];
        document.querySelectorAll('#sidenav-collapse-main > ul > .nav-item').forEach(function (g) {
            var group = ((g.querySelector(':scope > .nav-link .nav-link-text') || {}).textContent || '').trim();
            var cluster = '';
            g.querySelectorAll('.collapse > ul > li').forEach(function (li) {
                if (li.classList.contains('menu-group-label')) { cluster = li.textContent.trim(); return; }
                if (!li.classList.contains('menu-child-item')) return;
                var a = li.querySelector('a.nav-link');
                if (!a) return;
                var title = ((li.querySelector('.nav-link-text') || {}).textContent || '').trim();
                list.push({
                    url: a.getAttribute('href'), title: title, group: group, cluster: cluster,
                    icon: (li.querySelector('.menu-child-icon') || {}).innerHTML || '',
                    ft: fold(title + ' ' + (li.getAttribute('data-search') || '')), fg: fold(group + ' ' + cluster)
                });
            });
        });
        return list;
    }
    function scoreItem(it, words) {
        var sc = 0, inTitle = false;
        for (var i = 0; i < words.length; i++) {
            var w = words[i], p = it.ft.indexOf(w);
            if (p >= 0) inTitle = true;
            if (p === 0) sc += 30;                                   // đầu tên
            else if (p > 0 && it.ft.charAt(p - 1) === ' ') sc += 20; // đầu 1 chữ trong tên
            else if (p > 0) sc += 10;
            else if (it.fg.indexOf(w) >= 0) sc += 4;                 // khớp tên nhóm/cụm
            else return 0;                                           // phải khớp ĐỦ mọi chữ
        }
        return inTitle ? sc : 0;                                     // ít nhất 1 chữ phải nằm trong TÊN trang
    }
    var pal = null, palItems = [], palSel = 0, palShown = [];
    function buildPalette() {
        pal = document.createElement('div');
        pal.className = 'hr-pal';
        pal.innerHTML =
            '<div class="hr-pal-box" role="dialog" aria-modal="true">' +
                '<div class="hr-pal-in"><i class="material-symbols-rounded no-ic3d">search</i>' +
                    '<input type="text" autocomplete="off" spellcheck="false" placeholder="' + escHtml(PAL_LBL.ph) + '"><kbd>Esc</kbd></div>' +
                '<div class="hr-pal-list"></div>' +
                '<div class="hr-pal-foot">' + escHtml(PAL_LBL.hint) + '</div>' +
            '</div>';
        document.body.appendChild(pal);
        var input = pal.querySelector('input');
        input.addEventListener('input', function () { palSel = 0; renderPalette(this.value); });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown') { e.preventDefault(); palSel = Math.min(palSel + 1, palShown.length - 1); markSel(); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); palSel = Math.max(palSel - 1, 0); markSel(); }
            else if (e.key === 'Enter') { e.preventDefault(); var it = palShown[palSel]; if (it) location.href = it.url; }
            else if (e.key === 'Escape') { closePalette(); }
        });
        pal.addEventListener('mousedown', function (e) { if (e.target === pal) closePalette(); });
        pal.querySelector('.hr-pal-list').addEventListener('mousemove', function (e) {
            var r = e.target.closest('.hr-pal-row'); if (!r) return;
            var i = +r.getAttribute('data-i'); if (i !== palSel) { palSel = i; markSel(); }
        });
    }
    function rowHtml(it, i) {
        var where = it.group + (it.cluster ? ' › ' + it.cluster : '');
        return '<a class="hr-pal-row" data-i="' + i + '" href="' + escHtml(it.url) + '"><span class="hr-pal-ic">' + it.icon + '</span>' +
               '<span class="hr-pal-tx"><b>' + escHtml(it.title) + '</b><small>' + escHtml(where) + '</small></span>' +
               '<i class="material-symbols-rounded no-ic3d hr-pal-go">keyboard_return</i></a>';
    }
    function renderPalette(q) {
        var listEl = pal.querySelector('.hr-pal-list');
        var words = fold(q).split(/\s+/).filter(Boolean);
        var html = '';
        if (!words.length) {
            // Chưa gõ gì: hiện chức năng hay dùng (nếu có), không thì 8 mục đầu
            var top = topUsed(6).map(function (m) {
                return palItems.filter(function (x) { return x.url === m.url; })[0];
            }).filter(Boolean);
            palShown = top.length ? top : palItems.slice(0, 8);
            if (top.length) html += '<div class="hr-pal-sec">⭐ ' + escHtml(PAL_LBL.freq) + '</div>';
        } else {
            palShown = palItems.map(function (it) { return { it: it, s: scoreItem(it, words) }; })
                .filter(function (x) { return x.s > 0; })
                .sort(function (a, b) { return b.s - a.s || a.it.title.length - b.it.title.length; })
                .slice(0, 12).map(function (x) { return x.it; });
        }
        html += palShown.map(rowHtml).join('');
        listEl.innerHTML = palShown.length ? html : '<div class="hr-pal-none">' + escHtml(PAL_LBL.none) + '</div>';
        markSel();
    }
    function markSel() {
        pal.querySelectorAll('.hr-pal-row').forEach(function (r) {
            var on = +r.getAttribute('data-i') === palSel;
            r.classList.toggle('sel', on);
            if (on) r.scrollIntoView({ block: 'nearest' });
        });
    }
    function openPalette() {
        palItems = paletteItems();
        if (!palItems.length) return;
        if (!pal) buildPalette();
        pal.classList.add('open');
        var input = pal.querySelector('input');
        input.value = ''; palSel = 0; renderPalette('');
        setTimeout(function () { input.focus(); }, 10);
    }
    function closePalette() { if (pal) pal.classList.remove('open'); }
    window.hrOpenPalette = openPalette;

    function initPalette() {
        document.addEventListener('keydown', function (e) {
            var k = (e.key || '').toLowerCase();
            if ((e.ctrlKey || e.metaKey) && k === 'k') { e.preventDefault(); openPalette(); return; }
            // "/" mở nhanh khi không đang gõ trong ô nhập
            if (k === '/' && !e.ctrlKey && !e.metaKey && !/^(input|textarea|select)$/i.test((e.target.tagName || '')) && !e.target.isContentEditable) {
                if (document.querySelector('#sidenav-collapse-main .menu-child-item')) { e.preventDefault(); openPalette(); }
            }
        });
        // Nút "Tìm chức năng" trên thanh đầu trang (chỉ máy tính, tự chèn — không phải sửa 2 layout)
        var left = document.querySelector('.main-header-left');
        if (left && document.querySelector('#sidenav-collapse-main .menu-child-item')) {
            var mac = /Mac|iPhone|iPad/.test(navigator.platform);
            var b = document.createElement('button');
            b.type = 'button'; b.className = 'hr-pal-btn';
            b.innerHTML = '<i class="material-symbols-rounded no-ic3d">search</i><span>' + escHtml(PAL_LBL.btn) + '</span><kbd>' + (mac ? '⌘K' : 'Ctrl K') + '</kbd>';
            b.addEventListener('click', openPalette);
            left.appendChild(b);
        }
        // Ô tìm ở menu trái trên máy tính: bấm vào là mở bảng tìm nhanh (kết quả rõ hơn lọc trong menu)
        var side = document.getElementById('sidenavSearch');
        if (side) side.addEventListener('focus', function () {
            if (isMobile()) return;
            side.blur(); openPalette();
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initSidebarToggle();
        initPins();
        initSidenavSearch();
        trackUsage();
        renderFrequent();
        initRememberGroups();
        initGroupBadges();
        initPalette();
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
