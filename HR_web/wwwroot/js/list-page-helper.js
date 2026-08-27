/**
 * ListPageHelper — shared mobile behavior for "list pages" (stats-summary
 * row + filter bar + gradient card-header) used across Leave/GatePass/OT.
 *
 * Fully defensive / no-op on pages that don't have these elements — safe
 * to load globally, same spirit as MobileTableHelper (mobile-table.js).
 *
 * 1. Injects a "Bộ lọc" toggle button before every .filter-bar / .filter-wrap,
 *    collapsed by default on mobile (CSS in list-page-helper.css).
 * 2. Injects a "Xóa lọc" (clear filter) button as the last child of every
 *    .filter-bar / .filter-wrap that doesn't already ship its own reset
 *    control. Clicking it resets every input/select in the bar (and the
 *    date/month range pickers in the enclosing card-header) back to the
 *    value the server rendered — i.e. the page's default — keeps the
 *    page-size dropdown (#pageSizeSelect) untouched, then reloads page 1.
 * 3. Makes every .stats-card[data-filter-status] clickable: sets #filterStatus
 *    and dispatches a native 'change' event — every list page already binds
 *    $('#filterStatus').on('change', () => loadData(1)), so no per-page JS
 *    is required for the auto-filter behavior.
 */
(function (window, document) {
    'use strict';

    function debounce(fn, ms) {
        var t;
        return function () {
            clearTimeout(t);
            var args = arguments;
            t = setTimeout(function () { fn.apply(null, args); }, ms);
        };
    }

    function countActiveFilters(bar) {
        var count = 0;
        var controls = bar.querySelectorAll('input, select');
        controls.forEach(function (el) {
            if (el.id === 'pageSizeSelect') return;
            if (el.type === 'date' || el.type === 'month') return;
            if ((el.value || '').trim() !== '') count++;
        });
        return count;
    }

    function initFilterToggle(bar) {
        if (bar.dataset.lpInit) return;
        bar.dataset.lpInit = '1';

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'lp-filter-toggle';
        btn.innerHTML = '<i class="bi bi-funnel"></i> <span class="lp-label">Bộ lọc</span> <span class="lp-badge" style="display:none;"></span>';
        bar.parentNode.insertBefore(btn, bar);

        var updateBadge = debounce(function () {
            var n = countActiveFilters(bar);
            var badge = btn.querySelector('.lp-badge');
            if (n > 0) {
                badge.textContent = n;
                badge.style.display = '';
            } else {
                badge.style.display = 'none';
            }
        }, 150);

        btn.addEventListener('click', function () {
            var isOpen = bar.classList.toggle('lp-open');
            btn.classList.toggle('active', isOpen);
            var icon = btn.querySelector('i');
            if (icon) icon.className = isOpen ? 'bi bi-funnel-fill' : 'bi bi-funnel';
        });

        bar.addEventListener('change', updateBadge);
        bar.addEventListener('input', updateBadge);
        updateBadge();
    }

    /* ── "Xóa lọc" (clear filter) button ─────────────────────────── */

    var KEEP_IDS = ['pageSizeSelect'];

    function resetControl(el) {
        if (!el || KEEP_IDS.indexOf(el.id) !== -1) return;
        var tag = el.tagName;
        if (tag === 'INPUT') {
            var t = (el.getAttribute('type') || 'text').toLowerCase();
            if (t === 'hidden' || t === 'button' || t === 'submit' || t === 'file') return;
            if (t === 'checkbox' || t === 'radio') {
                if (el.checked !== el.defaultChecked) el.checked = el.defaultChecked;
            } else if (el.value !== el.defaultValue) {
                el.value = el.defaultValue;
            }
        } else if (tag === 'TEXTAREA') {
            if (el.value !== el.defaultValue) el.value = el.defaultValue;
        } else if (tag === 'SELECT') {
            var jq = window.jQuery;
            if (jq && jq(el).hasClass('select2-hidden-accessible')) {
                // select2 (dept / line / work …) — clear to placeholder without
                // firing the page's own 'change' cascade (that would re-trigger
                // loadData for every level); we reload once at the end.
                jq(el).val(null).trigger('change.select2');
            } else {
                var hasDefault = false;
                for (var i = 0; i < el.options.length; i++) {
                    el.options[i].selected = el.options[i].defaultSelected;
                    if (el.options[i].defaultSelected) hasDefault = true;
                }
                if (!hasDefault && el.options.length) el.selectedIndex = 0;
            }
        }
    }

    function safeReset(el) {
        try { resetControl(el); } catch (e) { /* one odd control shouldn't abort the clear */ }
    }

    function clearFilterBar(bar) {
        bar.querySelectorAll('input, select, textarea').forEach(safeReset);

        // Date / month range pickers usually live in the gradient card-header,
        // outside the .filter-bar — reset those too ("ngày về mặc định").
        var card = bar.closest('.card');
        if (card) {
            card.querySelectorAll('input[type="date"], input[type="month"]').forEach(function (el) {
                if (el.closest('.filter-bar') || el.closest('.filter-wrap')) return;
                safeReset(el);
            });
        }

        // Refresh the "Bộ lọc" toggle badge (it listens for 'change' on the bar).
        bar.dispatchEvent(new Event('change', { bubbles: true }));

        // Reload. Every Leave/GatePass/OT list page exposes a global loadData().
        if (typeof window.loadData === 'function') {
            window.loadData(1);
            return;
        }
        // Fallback for pages with a differently-named loader: nudge the handlers
        // the page bound to its own controls.
        var trigger = bar.querySelector('#filterStatus') ||
                      bar.querySelector('select, input:not([type="date"]):not([type="month"]):not([type="hidden"])');
        if (trigger) trigger.dispatchEvent(new Event('change', { bubbles: true }));
        var search = bar.querySelector('#searchInput, input[type="search"]');
        if (search) search.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function initClearBtn(bar) {
        if (bar.dataset.lpClearInit) return;
        bar.dataset.lpClearInit = '1';

        // Skip bars that already ship their own reset control.
        if (bar.querySelector('[data-clear-filter], [onclick*="resetFilter"], [onclick*="clearFilter"]')) return;

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn btn-sm btn-outline-secondary mb-0 lp-clear-filter';
        btn.setAttribute('data-clear-filter', '');
        btn.title = 'Xóa bộ lọc';
        btn.innerHTML = '<i class="bi bi-x-circle"></i>';
        btn.addEventListener('click', function () { clearFilterBar(bar); });

        // When the bar is itself a Bootstrap .row, wrap the button in a column
        // so it lines up with the other grid cells instead of overflowing.
        var node = btn;
        if (bar.classList.contains('row')) {
            var col = document.createElement('div');
            col.className = 'col-auto d-flex align-items-end ms-auto';
            col.appendChild(btn);
            node = col;
        }

        var anchor = bar.querySelector('.ms-auto');
        if (anchor && node === btn) bar.insertBefore(node, anchor);
        else bar.appendChild(node);
    }

    function initStatCards() {
        var cards = document.querySelectorAll('.stats-card[data-filter-status]');
        if (!cards.length) return;
        var filterStatus = document.getElementById('filterStatus');
        if (!filterStatus) return;

        cards.forEach(function (card) {
            card.addEventListener('click', function () {
                var value = card.dataset.filterStatus;
                var isActive = card.classList.contains('lp-active');

                // Bấm lại thẻ đang active (trừ thẻ "Tổng" value rỗng) -> bỏ chọn, hiện lại tất cả
                if (isActive && value !== '') value = '';

                filterStatus.value = value;
                cards.forEach(function (c) { c.classList.remove('lp-active'); });
                if (value !== '' || card.dataset.filterStatus === '') card.classList.add('lp-active');

                filterStatus.dispatchEvent(new Event('change', { bubbles: true }));
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('.filter-bar, .filter-wrap').forEach(function (bar) {
            initFilterToggle(bar);
            initClearBtn(bar);
        });
        initStatCards();
    });

}(window, document));
