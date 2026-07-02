// =============================================================
// Home page — Employee/Manager/Expat/Admin
// - Load summary AJAX + polling 60s (Manager/Expat/Admin)
// - Banner X toggle (localStorage)
// - Birthday confetti seen flag (localStorage per day)
// - Payday dismiss (per month)
// - Team birthday modal
// =============================================================

(function () {
    'use strict';

    const $  = (sel, root) => (root || document).querySelector(sel);
    const $$ = (sel, root) => Array.from((root || document).querySelectorAll(sel));

    // Root URL injected từ view (Razor @Url.Content("~/")) — hỗ trợ deploy dưới virtual dir
    const ROOT = ((window.__HR_HOME__ && window.__HR_HOME__.rootUrl) || '/').replace(/\/+$/, '') + '/';

    // ─── 1. BIRTHDAY BANNER ────────────────────────────────────
    (function initBirthday() {
        const el = $('#homeBirthdayBanner');
        if (!el) return;

        const today = el.dataset.today;
        const seenKey     = 'home_birthday_seen_' + today;
        const dismissKey  = 'home_birthday_dismissed_' + today;

        if (localStorage.getItem(dismissKey) === '1') {
            el.classList.add('home-birthday-dismissed');
            return;
        }
        if (localStorage.getItem(seenKey) === '1') {
            el.classList.add('home-birthday-seen'); // tắt confetti animation
        } else {
            localStorage.setItem(seenKey, '1'); // đánh dấu đã xem lần đầu
        }

        const btn = $('.home-birthday-close', el);
        if (btn) {
            btn.addEventListener('click', () => {
                localStorage.setItem(dismissKey, '1');
                el.classList.add('home-birthday-dismissed');
            });
        }
    })();

    // ─── 2. PAYDAY BANNER ──────────────────────────────────────
    (function initPayday() {
        const el = $('#homePaydayBanner');
        if (!el) return;

        const month = el.dataset.month;
        const dismissKey = 'home_payday_dismissed_' + month;
        if (localStorage.getItem(dismissKey) === '1') {
            el.classList.add('home-payday-dismissed');
            return;
        }
        const btn = $('.home-payday-close', el);
        if (btn) {
            btn.addEventListener('click', () => {
                localStorage.setItem(dismissKey, '1');
                el.classList.add('home-payday-dismissed');
            });
        }
    })();

    // ─── 3. HERO BANNER TOGGLE ─────────────────────────────────
    (function initHeroBanner() {
        const el = $('#homeHeroBanner');
        if (!el) return;

        const key = 'home_banner_collapsed';
        if (localStorage.getItem(key) === '1') {
            el.classList.add('home-hero-collapsed');
        }
        const btn = $('.home-hero-toggle', el);
        if (btn) {
            btn.addEventListener('click', (e) => {
                e.preventDefault();
                el.classList.toggle('home-hero-collapsed');
                const collapsed = el.classList.contains('home-hero-collapsed');
                localStorage.setItem(key, collapsed ? '1' : '0');
            });
        }
    })();

    // ─── 4. SUMMARY CARD (polling 60s, visibility API) ─────────
    (function initSummary() {
        const card = $('#homeSummaryCard');
        if (!card) return;

        const body    = $('[data-summary-body]', card);
        const tpl     = $('#homeSummaryTemplate');
        const asofEl  = () => $('[data-summary-asof]', card);

        let timer = null;

        async function fetchSummary(force) {
            try {
                const res = await fetch(ROOT + 'Home/Summary' + (force ? '?force=1' : ''), {
                    headers: { 'X-Requested-With': 'XMLHttpRequest' },
                    credentials: 'same-origin'
                });
                if (!res.ok) throw new Error('HTTP ' + res.status);
                const json = await res.json();
                if (json.success && json.data) {
                    renderSummary(json.data);
                }
            } catch (e) {
                console.warn('[home] summary fetch error:', e);
            }
        }

        function renderSummary(data) {
            // Nếu chưa render lần nào, clone template vào body
            if (!body.querySelector('.home-summary-grid')) {
                body.innerHTML = '';
                body.appendChild(tpl.content.cloneNode(true));
            }
            setNum('leave',     data.LEAVE_PENDING);
            setNum('gp',        data.GP_PENDING);
            setNum('ot-need',   data.OT_NEED_SIGN);
            setNum('ot-signed', data.OT_SIGNED);
            setNum('ot-total',  data.OT_TOTAL);
            setNum('bd',        data.TEAM_BIRTHDAY_COUNT);

            const a = asofEl();
            if (a && data.AS_OF) {
                a.textContent = formatAsOf(data.AS_OF);
            }
        }

        function setNum(key, val) {
            const el = body.querySelector('[data-kpi="' + key + '"]');
            if (el) el.textContent = (val || 0).toString();
        }

        function formatAsOf(iso) {
            try {
                const d = new Date(iso);
                const lang = card.dataset.lang || 'vi';
                const isEn = lang === 'en';
                const locale = isEn ? 'en-US' : 'vi-VN';
                const prefix = isEn ? 'Updated ' : 'Cập nhật ';
                return prefix + d.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', second: '2-digit' });
            } catch { return ''; }
        }

        function startPolling() {
            stopPolling();
            timer = setInterval(() => fetchSummary(false), 60000);
        }
        function stopPolling() { if (timer) { clearInterval(timer); timer = null; } }

        // Manual refresh button
        const refreshBtn = $('.home-summary-refresh', card);
        if (refreshBtn) {
            refreshBtn.addEventListener('click', async () => {
                refreshBtn.classList.add('refreshing');
                await fetchSummary(true);
                setTimeout(() => refreshBtn.classList.remove('refreshing'), 600);
            });
        }

        // Pause polling khi tab ẩn
        document.addEventListener('visibilitychange', () => {
            if (document.hidden) stopPolling();
            else { fetchSummary(false); startPolling(); }
        });

        // Init
        fetchSummary(false);
        startPolling();
    })();

    // ─── 5. TEAM BIRTHDAY MODAL ────────────────────────────────
    (function initTeamBirthdayModal() {
        const modal = $('#homeTeamBirthdayModal');
        if (!modal) return;

        const modalBody = $('[data-modal-body]', modal);
        const card      = $('#homeSummaryCard');
        const emptyLbl  = card ? (card.dataset.labelEmpty || 'Không có SN nào hôm nay') : 'Không có SN nào hôm nay';
        const errorLbl  = card ? (card.dataset.labelError || 'Không tải được danh sách') : 'Không tải được danh sách';

        function openModal() {
            modal.hidden = false;
            loadList();
        }
        function closeModal() { modal.hidden = true; }

        async function loadList() {
            try {
                const res = await fetch(ROOT + 'Home/TeamBirthday', {
                    headers: { 'X-Requested-With': 'XMLHttpRequest' },
                    credentials: 'same-origin'
                });
                if (!res.ok) throw new Error('HTTP ' + res.status);
                const json = await res.json();
                if (json.success && Array.isArray(json.data) && json.data.length > 0) {
                    modalBody.innerHTML = json.data.map(renderItem).join('');
                } else {
                    modalBody.innerHTML = '<div class="home-modal-loading">' + escapeHtml(emptyLbl) + '</div>';
                }
            } catch (e) {
                modalBody.innerHTML = '<div class="home-modal-loading">' + escapeHtml(errorLbl) + '</div>';
                console.warn('[home] team birthday fetch error:', e);
            }
        }

        function renderItem(item) {
            const initial = (item.CNAME || '?').charAt(0).toUpperCase();
            const meta = [item.DEPTCD, item.LINECD, item.WORKCD].filter(x => x).join(' · ');
            return `<div class="home-bd-item">
                <div class="home-bd-avatar">${initial}</div>
                <div>
                    <div class="home-bd-name vni-font">${escapeHtml(item.CNAME || '')}</div>
                    <div class="home-bd-meta">${escapeHtml(meta)}</div>
                </div>
            </div>`;
        }

        function escapeHtml(s) {
            return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
        }

        document.addEventListener('click', (e) => {
            const opener = e.target.closest('[data-open="team-birthday"]');
            if (opener) { e.preventDefault(); openModal(); return; }
            const closer = e.target.closest('[data-close="team-birthday"]');
            if (closer) { e.preventDefault(); closeModal(); return; }
        });

        // ESC to close
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && !modal.hidden) closeModal();
        });
    })();

})();
