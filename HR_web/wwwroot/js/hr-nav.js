/* ================================================================
   hr-nav.js — JS cho menu điều hướng (đóng/mở sidebar, tìm kiếm trong
   sidenav, ghim mục yêu thích). Trước đây nằm rải rác inline trong
   _Layout.cshtml và _SideNav.cshtml — tách ra đây cho dễ sửa (yêu cầu
   2026-10-13: menu không thân thiện, nhất là mobile, khó sửa vì
   CSS/JS lẫn trong Razor).
   ================================================================ */

(function () {
    'use strict';

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
            btn.title = pinned ? 'Bỏ ghim' : 'Ghim vào Yêu thích';
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
                        '<span class="menu-group-dot" style="background:#f59e0b;"></span>Yêu thích' +
                    '</div><ul class="navbar-nav">';
        pins.forEach(function (p) {
            html += '<li class="nav-item menu-child-item" data-search="' + escHtml((p.title || '').toLowerCase()) + '">' +
                        '<div class="d-flex align-items-center">' +
                            '<a class="nav-link text-dark rounded px-2 py-1 flex-grow-1" href="' + escHtml(p.url) + '">' +
                                '<span class="menu-child-icon"><i class="material-symbols-rounded">' + escHtml(p.icon || 'star') + '</i></span>' +
                                '<span class="nav-link-text">' + escHtml(p.title) + '</span>' +
                            '</a>' +
                            '<button type="button" class="menu-pin-btn pinned" data-pin-url="' + escHtml(p.url) + '" data-pin-title="' + escHtml(p.title) + '" data-pin-icon="' + escHtml(p.icon || '') + '" title="Bỏ ghim">' +
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
