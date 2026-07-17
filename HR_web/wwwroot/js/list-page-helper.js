/**
 * ListPageHelper — shared mobile behavior for "list pages" (stats-summary
 * row + filter bar + gradient card-header) used across Leave/GatePass/OT.
 *
 * Fully defensive / no-op on pages that don't have these elements — safe
 * to load globally, same spirit as MobileTableHelper (mobile-table.js).
 *
 * 1. Injects a "Bộ lọc" toggle button before every .filter-bar / .filter-wrap,
 *    collapsed by default on mobile (CSS in list-page-helper.css).
 * 2. Makes every .stats-card[data-filter-status] clickable: sets #filterStatus
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
        document.querySelectorAll('.filter-bar, .filter-wrap').forEach(initFilterToggle);
        initStatCards();
    });

}(window, document));
