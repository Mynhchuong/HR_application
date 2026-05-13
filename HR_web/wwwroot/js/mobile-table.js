/**
 * MobileTableHelper — transforms <table> rows into tap-to-detail cards on mobile.
 *
 * Usage:
 *   1. Add data-mobile="primary|detail|hidden" to each <th> in thead.
 *      Optionally add data-mobile-slot="code|name|badge" to control card layout.
 *   2. In JS: MobileTableHelper.init('myTbodyId');
 *   3. After each AJAX data update: MobileTableHelper.refresh('myTbodyId');
 *
 * Slots:
 *   code  → small identifier chip (top-left of card)
 *   name  → bold primary text (next to code)
 *   badge → inline badges row (bottom of card)
 *   (none) → treated same as badge
 *
 * Roles:
 *   primary → shown in card body
 *   detail  → shown in tap-to-open modal only
 *   hidden  → not shown anywhere on mobile
 */
(function (window, document) {
    'use strict';

    var _instances = {};
    var _modalEl = null;

    function _ensureModal() {
        if (_modalEl) return;
        var div = document.createElement('div');
        div.innerHTML = [
            '<div class="modal fade" id="mtDetailModal" tabindex="-1" aria-hidden="true">',
            '  <div class="modal-dialog modal-dialog-centered" style="max-width:340px">',
            '    <div class="modal-content border-0 shadow-lg mt-modal-content">',
            '      <div class="modal-header border-0 pb-0 pt-3 px-4">',
            '        <span class="mt-modal-icon"><i class="bi bi-card-list"></i></span>',
            '        <h6 class="modal-title fw-bold ms-2 text-primary">Chi tiết</h6>',
            '        <button type="button" class="btn-close ms-auto" data-bs-dismiss="modal"></button>',
            '      </div>',
            '      <div class="modal-body px-4 pt-2 pb-4" id="mtDetailBody"></div>',
            '    </div>',
            '  </div>',
            '</div>'
        ].join('');
        document.body.appendChild(div.firstElementChild);
        _modalEl = document.getElementById('mtDetailModal');
    }

    function _getColConfig(tbodyId) {
        var tbody = document.getElementById(tbodyId);
        if (!tbody) return [];
        var table = tbody.closest('table');
        if (!table) return [];
        var ths = table.querySelectorAll('thead th');
        return Array.prototype.map.call(ths, function (th, i) {
            return {
                index: i,
                label: (th.dataset.mobileLabel || th.textContent).trim(),
                role:  th.dataset.mobile     || 'detail',
                slot:  th.dataset.mobileSlot || null
            };
        });
    }

    function _showModal(detailData) {
        _ensureModal();
        var body = document.getElementById('mtDetailBody');
        body.innerHTML = detailData.map(function (d) {
            var valClass = 'mt-detail-value' + (d.slot === 'name' ? ' vni-font' : '');
            return '<div class="mt-detail-row">' +
                '<div class="mt-detail-label">' + d.label + '</div>' +
                '<div class="' + valClass + '">' + d.html + '</div>' +
                '</div>';
        }).join('');
        bootstrap.Modal.getOrCreateInstance(_modalEl).show();
    }

    function _buildCards(tbodyId, colCfg) {
        var tbody = document.getElementById(tbodyId);
        if (!tbody) return;

        var containerId = 'mt-list-' + tbodyId;
        var container   = document.getElementById(containerId);
        if (!container) {
            container = document.createElement('div');
            container.id        = containerId;
            container.className = 'mt-card-list';
            var tableResponsive = tbody.closest('.table-responsive');
            if (tableResponsive) {
                tableResponsive.insertAdjacentElement('afterend', container);
            } else {
                tbody.closest('table').insertAdjacentElement('afterend', container);
            }
        }

        var rows = tbody.querySelectorAll('tr');
        if (!rows.length) { container.innerHTML = ''; return; }

        // Loading or empty state (single cell with colspan)
        var firstTd = rows[0].querySelector('td[colspan]');
        if (firstTd) {
            container.innerHTML = '<div class="mt-empty">' + firstTd.innerHTML + '</div>';
            return;
        }

        var codeCols    = colCfg.filter(function (c) { return c.role === 'primary' && c.slot === 'code'; });
        var nameCols    = colCfg.filter(function (c) { return c.role === 'primary' && c.slot === 'name'; });
        var badgeCols   = colCfg.filter(function (c) { return c.role === 'primary' && c.slot !== 'code' && c.slot !== 'name'; });
        var detailCols  = colCfg.filter(function (c) { return c.role !== 'hidden'; });

        var fragment = document.createDocumentFragment();

        Array.prototype.forEach.call(rows, function (tr) {
            var cells = tr.querySelectorAll('td');
            if (!cells.length) return;

            var get = function (i) { return cells[i] ? cells[i].innerHTML : ''; };

            var codeHtml  = codeCols.map(function (c) {
                return '<span class="mt-chip">' + get(c.index) + '</span>';
            }).join('');

            var nameHtml  = nameCols.map(function (c) {
                return '<span class="mt-name vni-font">' + get(c.index) + '</span>';
            }).join('');

            var badgesHtml = badgeCols.map(function (c) {
                return '<span class="mt-badge-item">' + get(c.index) + '</span>';
            }).join('');

            var detailData = detailCols.map(function (c) {
                return { label: c.label, html: get(c.index), slot: c.slot };
            });

            var headerRow  = (codeHtml || nameHtml) ? '<div class="mt-header-row">' + codeHtml + nameHtml + '</div>' : '';
            var badgesRow  = badgesHtml ? '<div class="mt-badges-row">' + badgesHtml + '</div>' : '';

            var card = document.createElement('div');
            card.className   = 'mt-card';
            card.innerHTML   =
                '<div class="mt-card-body">' + headerRow + badgesRow + '</div>' +
                '<button class="mt-more-btn" aria-label="Chi tiết"><i class="bi bi-chevron-right"></i></button>';

            var openDetail = function () { _showModal(detailData); };
            card.addEventListener('click', openDetail);
            card.querySelector('.mt-more-btn').addEventListener('click', function (e) {
                e.stopPropagation();
                openDetail();
            });

            fragment.appendChild(card);
        });

        container.innerHTML = '';
        container.appendChild(fragment);
    }

    function _applyLayout(tbodyId) {
        var cfg = _instances[tbodyId];
        if (!cfg) return;
        var tbody = document.getElementById(tbodyId);
        if (!tbody) return;

        var isMobile        = window.innerWidth <= cfg.breakpoint;
        var tableResponsive = tbody.closest('.table-responsive');
        var cardList        = document.getElementById('mt-list-' + tbodyId);

        if (tableResponsive) tableResponsive.style.display = isMobile ? 'none' : '';
        if (cardList)        cardList.style.display        = isMobile ? ''     : 'none';
    }

    /**
     * Register a tbody for mobile card transformation.
     * @param {string} tbodyId  - id of the <tbody> element
     * @param {object} [opts]   - { breakpoint: 767 }
     */
    function init(tbodyId, opts) {
        opts = opts || {};
        _instances[tbodyId] = { breakpoint: opts.breakpoint || 767 };
        _ensureModal();

        var timer;
        window.addEventListener('resize', function () {
            clearTimeout(timer);
            timer = setTimeout(function () { _applyLayout(tbodyId); }, 200);
        });
    }

    /**
     * Call after every tbody update (AJAX load, empty state, error state).
     * @param {string} tbodyId
     */
    function refresh(tbodyId) {
        if (!_instances[tbodyId]) return;
        _buildCards(tbodyId, _getColConfig(tbodyId));
        _applyLayout(tbodyId);
    }

    window.MobileTableHelper = { init: init, refresh: refresh };

}(window, document));
