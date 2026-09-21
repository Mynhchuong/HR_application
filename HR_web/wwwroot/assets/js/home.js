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

    // ─── 3. HERO BANNER POPUP (mỗi phiên login hiện 1 lần) ─────
    (function initHeroPopup() {
        const el = $('#homeHeroPopup');
        if (!el) return;

        // sessionStorage: đóng browser/tab hoặc logout → clear → login vô lại thấy popup
        const key = 'home_hero_popup_dismissed';

        // Đã đóng trong phiên login này → không show
        if (sessionStorage.getItem(key) === '1') return;

        // Show sau 300ms cho page settle
        setTimeout(() => {
            el.hidden = false;
            document.body.classList.add('home-hero-popup-open');
            // iOS Safari đôi khi không autoplay khi element trước đó hidden → gọi play explicit
            const v = el.querySelector('#homeHeroVideo');
            if (v) { const p = v.play(); if (p && p.catch) p.catch(() => {}); }
        }, 300);

        function dismiss() {
            const v = el.querySelector('#homeHeroVideo');
            if (v) { try { v.pause(); } catch (_) {} }
            el.hidden = true;
            document.body.classList.remove('home-hero-popup-open');
            sessionStorage.setItem(key, '1');
        }

        el.querySelectorAll('[data-close="hero-popup"]').forEach(x =>
            x.addEventListener('click', (ev) => { ev.preventDefault(); dismiss(); }));

        // Click ảnh có link → đóng luôn (giống Shopee: navigate rồi đóng)
        const link = el.querySelector('.home-hero-popup-link');
        if (link) link.addEventListener('click', () => sessionStorage.setItem(key, '1'));

        // Video banner: khi kết thúc → show overlay "Xem lại" / "Đóng"
        const video = el.querySelector('#homeHeroVideo');
        const endedOverlay = el.querySelector('#homeHeroVideoEnded');
        if (video && endedOverlay) {
            video.addEventListener('ended', () => { endedOverlay.hidden = false; });
            video.addEventListener('play',  () => { endedOverlay.hidden = true;  });
            const replayBtn = el.querySelector('[data-hero-replay]');
            if (replayBtn) replayBtn.addEventListener('click', () => {
                endedOverlay.hidden = true;
                try { video.currentTime = 0; video.play(); } catch (_) {}
            });
        }

        // ESC key
        document.addEventListener('keydown', (ev) => {
            if (ev.key === 'Escape' && !el.hidden) dismiss();
        });
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
                } else {
                    renderError(json.message);
                }
            } catch (e) {
                console.warn('[home] summary fetch error:', e);
                renderError(e && e.message);
            }
        }

        function renderError(msg) {
            // Chỉ show error khi chưa render lần nào (giữ data cũ khi polling lỗi tạm thời)
            if (body.querySelector('.home-summary-grid')) return;
            const errLbl = card.dataset.labelError || 'Không tải được dữ liệu';
            const wrap = document.createElement('div');
            wrap.className = 'home-summary-loading';
            wrap.textContent = '⚠️ ' + errLbl + (msg ? ' (' + msg + ')' : '');
            body.innerHTML = '';
            body.appendChild(wrap);
        }

        function renderSummary(data) {
            // Nếu chưa render lần nào, clone template vào body
            if (!body.querySelector('.home-summary-grid')) {
                body.innerHTML = '';
                body.appendChild(tpl.content.cloneNode(true));
            }
            setNum('leave',        data.LEAVE_PENDING);
            setNum('gp',           data.GP_PENDING);
            setNum('leave-today',  data.LEAVE_TODAY_TOTAL);
            setNum('gp-today',     data.GP_TODAY_TOTAL);
            setNum('ot-need',      data.OT_NEED_SIGN);
            setNum('ot-signed',    data.OT_SIGNED);
            setNum('ot-total',     data.OT_TOTAL);
            setNum('bd',           data.TEAM_BIRTHDAY_COUNT);
            setNum('leave-doc-missing', data.LEAVE_DOC_MISSING_COUNT);
            setNum('hr-msg',       data.HR_UNREAD_MSG);
            setNum('hr-inq',       data.HR_OPEN_INQ);
            setNum('hr-bull',      data.HR_NEW_BULLETIN_CMT);
            // Training today — chỉ show card khi > 0 (tránh clutter khi không có session)
            setNum('training-today', data.TRAINING_TODAY_TOTAL);
            const trainingCard = body.querySelector('[data-kpi-card="training-today"]');
            if (trainingCard) trainingCard.hidden = !(data.TRAINING_TODAY_TOTAL > 0);

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
            const orgParts = [item.DEPT_NAME, item.LINE_NAME, item.WORK_NAME].filter(x => x).join(' · ');
            return `<div class="home-bd-item">
                <div class="home-bd-avatar">${initial}</div>
                <div>
                    <div class="home-bd-name vni-font">${escapeHtml(item.CNAME || '')}</div>
                    <div class="home-bd-meta"><strong>${escapeHtml(item.EMPCD || '')}</strong>${orgParts ? ' · ' + escapeHtml(orgParts) : ''}</div>
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

    // ─── 6. LEAVE DOC MISSING MODAL ─────────────────────────────
    (function initLeaveDocMissingModal() {
        const modal = $('#homeLeaveDocMissingModal');
        if (!modal) return;

        const modalBody = $('[data-modal-body]', modal);
        const card      = $('#homeSummaryCard');
        const lang      = card ? (card.dataset.lang || 'vi') : 'vi';
        const isEn      = lang === 'en';
        const emptyLbl  = isEn ? 'No pending documents' : 'Không có đơn nào chưa nộp giấy tờ';
        const errorLbl  = card ? (card.dataset.labelError || 'Không tải được danh sách') : 'Không tải được danh sách';
        // Danh sách này phải khớp LEAVE_TYPE IN (...) trong HomeSummaryService.CountLeaveDocMissingAsync/
        // GetLeaveDocMissingListAsync (6 loại: DT,DC,VS,KT,SI,DS) — thiếu SI/DS ở đây khiến 2 loại đó
        // hiện thẳng mã "SI"/"DS" thay vì tên đầy đủ (phát hiện khi rà lại toàn bộ các file liên quan
        // tính năng nộp giấy tờ, 2026-09-10).
        const leaveTypeNames = isEn
            ? { DT: 'Funeral', DC: 'Wedding', VS: 'Childbirth', KT: 'Prenatal checkup', SI: 'Sick leave (with certificate)', DS: 'Postnatal recovery' }
            : { DT: 'Đám tang', DC: 'Đám cưới', VS: 'Vợ sanh', KT: 'Khám thai', SI: 'Bệnh có giấy', DS: 'Dưỡng sức' };
        // PARTIALLY_SUBMITTED (thêm 2026-09-10, xem LeaveListForHR/LeaveListForClerk) trước đây rớt vào
        // nhánh mặc định "Chưa nộp" — sai vì đơn đã nộp một phần rồi, không phải chưa nộp gì cả.
        const docStatusLabel = isEn
            ? { RESUBMIT_REQUESTED: 'Resubmit requested', PARTIALLY_SUBMITTED: 'Partially submitted' }
            : { RESUBMIT_REQUESTED: 'Yêu cầu nộp lại', PARTIALLY_SUBMITTED: 'Nộp 1 phần' };
        const docStatusDefault = isEn ? 'Not submitted' : 'Chưa nộp';

        function openModal() {
            modal.hidden = false;
            loadList();
        }
        function closeModal() { modal.hidden = true; }

        async function loadList() {
            try {
                const res = await fetch(ROOT + 'Home/LeaveDocMissing', {
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
                console.warn('[home] leave doc missing fetch error:', e);
            }
        }

        function formatDate(s) {
            if (!s) return '-';
            const d = new Date(s);
            if (isNaN(d)) return '-';
            return ('0' + d.getDate()).slice(-2) + '/' + ('0' + (d.getMonth() + 1)).slice(-2) + '/' + d.getFullYear();
        }

        function renderItem(item) {
            const initial = (item.CNAME || '?').charAt(0).toUpperCase();
            const orgParts = [item.DEPT_NAME, item.LINE_NAME, item.WORK_NAME].filter(x => x).join(' · ');
            const ltName = leaveTypeNames[item.LEAVE_TYPE] || item.LEAVE_TYPE || '';
            const isResubmit = item.DOC_STATUS === 'RESUBMIT_REQUESTED';
            const statusText = docStatusLabel[item.DOC_STATUS] || docStatusDefault;
            const statusBadge = `<span class="home-bd-docstatus${isResubmit ? ' is-resubmit' : ''}">${escapeHtml(statusText)}</span>`;
            return `<div class="home-bd-item">
                <div class="home-bd-avatar">${initial}</div>
                <div>
                    <div class="home-bd-name vni-font">${escapeHtml(item.CNAME || '')} ${statusBadge}</div>
                    <div class="home-bd-meta"><strong>${escapeHtml(item.EMPCD || '')}</strong>${orgParts ? ' · ' + escapeHtml(orgParts) : ''}</div>
                    <div class="home-bd-meta">${escapeHtml(ltName)} · ${formatDate(item.FROM_DATE)} – ${formatDate(item.TO_DATE)}</div>
                </div>
            </div>`;
        }

        function escapeHtml(s) {
            return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
        }

        document.addEventListener('click', (e) => {
            const opener = e.target.closest('[data-open="leave-doc-missing"]');
            if (opener) { e.preventDefault(); openModal(); return; }
            const closer = e.target.closest('[data-close="leave-doc-missing"]');
            if (closer) { e.preventDefault(); closeModal(); return; }
        });

        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && !modal.hidden) closeModal();
        });
    })();

    // ─────────────────────────────────────────────────────────
    // My Calendar — mini lịch cá nhân, click ngày show chi tiết
    // ─────────────────────────────────────────────────────────
    (function initMyCalendar() {
        const root = document.getElementById('homeMyCalendar');
        if (!root) return;

        const grid    = root.querySelector('#mycalGrid');
        const titleEl = root.querySelector('#mycalTitle');
        const rootUrl = (window.__HR_HOME__ && window.__HR_HOME__.rootUrl) || '/';
        const modalEl = document.getElementById('mycalDetailModal');
        const detTitle = document.getElementById('mycalDetailTitle');
        const detBody  = document.getElementById('mycalDetailBody');

        // Bootstrap gắn aria-hidden="true" lên modal khi đóng nhưng không tự bỏ focus khỏi nút bên
        // trong (vd nút đóng) trước đó — trình duyệt cảnh báo "Blocked aria-hidden..." trong console.
        // Chủ động bỏ focus (blur) ngay khi modal bắt đầu đóng để tránh cảnh báo này.
        if (modalEl) {
            modalEl.addEventListener('hide.bs.modal', () => {
                if (modalEl.contains(document.activeElement)) {
                    document.activeElement.blur();
                }
            });
        }

        const [initY, initM] = (root.dataset.initMonth || '').split('-').map(n => parseInt(n, 10));
        let curY = initY || new Date().getFullYear();
        let curM = initM || (new Date().getMonth() + 1);
        let eventsByDate = {}; // { 'YYYY-MM-DD': [ {TYPE, LABEL, DETAIL}, ... ] }

        // Đăng ký từng loại sự kiện lịch — thêm loại MỚI trong tương lai chỉ cần thêm 1 dòng ở
        // đây (icon hiện + priority sắp xếp), KHÔNG phải sửa gì trong renderGrid/showDetail bên
        // dưới. priority nhỏ hơn = quan trọng hơn, ưu tiên hiện trước khi 1 ngày có nhiều loại
        // (ô ngày trên điện thoại rất nhỏ, không thể hiện hết 5-6 icon cùng lúc).
        const EVENT_TYPES = {
            ATT_MISSING: { icon: '🔴', priority: 1 },  // cần hành động ngay -> ưu tiên cao nhất
            PAYDAY:      { icon: '⭐', priority: 2 },  // ngày 10 mỗi tháng, tin vui nên ưu tiên cao
            TRAINING:    { icon: '📚', priority: 3 },  // buổi học phải tham gia
            GIFT:        { icon: '🎁', priority: 4 },
            LEAVE:       { icon: '🌴', priority: 5 },
            ASSIGN:      { icon: '📅', priority: 6 },
            GP:          { icon: '🚪', priority: 7 },
            OT:          { icon: '⏱️', priority: 8 },
        };
        const MAX_ICONS_PER_CELL = 3; // hơn số này thì gộp thành chip "+N", bấm vào ô vẫn xem đủ
        function iconFor(type) { return EVENT_TYPES[type]?.icon || '📌'; }
        // Nhãn trạng thái nộp giấy tờ trong popup chi tiết ngày — DOC_STATUS giờ tính RIÊNG CHO
        // TỪNG NGÀY (không phải trạng thái chung cả đơn nữa), nên nhãn cũng nói rõ "ngày này".
        const DOC_STATUS_LABEL = {
            SUBMITTED: 'Đã nộp giấy tờ ngày này',
            RESUBMIT_REQUESTED: 'HR yêu cầu nộp lại giấy tờ'
        };
        const DOC_STATUS_DEFAULT = 'Chưa nộp giấy tờ ngày này';

        function pad(n) { return n < 10 ? '0' + n : '' + n; }
        function todayKey() {
            const t = new Date();
            return `${t.getFullYear()}-${pad(t.getMonth()+1)}-${pad(t.getDate())}`;
        }

        function renderGrid() {
            // Clear cells (keep DoW headers = first 7 children)
            while (grid.children.length > 7) grid.removeChild(grid.lastChild);

            titleEl.textContent = `Tháng ${curM}/${curY}`;

            const firstDow = new Date(curY, curM - 1, 1).getDay(); // 0=CN
            const daysInMonth = new Date(curY, curM, 0).getDate();
            const tKey = todayKey();

            for (let i = 0; i < firstDow; i++) {
                const empty = document.createElement('div');
                empty.className = 'mycal-cell mycal-empty';
                grid.appendChild(empty);
            }

            for (let d = 1; d <= daysInMonth; d++) {
                const key = `${curY}-${pad(curM)}-${pad(d)}`;
                const dow = new Date(curY, curM - 1, d).getDay();
                const evs = eventsByDate[key] || [];

                const cell = document.createElement('div');
                cell.className = 'mycal-cell';
                if (dow === 0) cell.classList.add('mycal-sunday');
                if (key === tKey) cell.classList.add('mycal-today');
                if (evs.length) cell.classList.add('mycal-has-event');
                cell.dataset.date = key;

                const numEl = document.createElement('div');
                numEl.className = 'mycal-daynum';
                numEl.textContent = d;
                cell.appendChild(numEl);

                if (evs.length) {
                    // Công nhân phản hồi chấm màu nhỏ khó thấy trên điện thoại -> đổi qua emoji.
                    // Sắp theo priority (EVENT_TYPES) rồi cắt còn MAX_ICONS_PER_CELL — 1 ngày có
                    // 4-5 loại sự kiện vẫn không tràn ô, phần dư gộp thành chip "+N" (bấm vào ô
                    // vẫn mở popup xem đủ mọi loại, chỉ ô lịch là rút gọn).
                    const dots = document.createElement('div');
                    dots.className = 'mycal-dots';
                    const types = Array.from(new Set(evs.map(x => x.TYPE)))
                        .sort((a, b) => (EVENT_TYPES[a]?.priority ?? 99) - (EVENT_TYPES[b]?.priority ?? 99));

                    types.slice(0, MAX_ICONS_PER_CELL).forEach(t => {
                        const span = document.createElement('span');
                        span.className = `mc-emoji mc-emoji-${t}`;
                        span.textContent = iconFor(t);
                        dots.appendChild(span);
                    });
                    const hidden = types.length - MAX_ICONS_PER_CELL;
                    if (hidden > 0) {
                        const more = document.createElement('span');
                        more.className = 'mc-more';
                        more.textContent = `+${hidden}`;
                        dots.appendChild(more);
                    }
                    cell.appendChild(dots);

                    // Chấm góc: ngày có nghỉ phải nộp giấy tờ (SI/DT/DC/VS/DS/KT) — xanh nếu tất cả
                    // đã nộp đủ, đỏ nếu còn thiếu ít nhất 1 loại. Giúp NV lướt mắt biết ngay chưa nộp.
                    const docEvs = evs.filter(x => x.DOC_REQUIRED);
                    if (docEvs.length) {
                        const allDone = docEvs.every(x => x.DOC_STATUS === 'SUBMITTED');
                        const badge = document.createElement('span');
                        badge.className = `mc-doc-badge ${allDone ? 'mc-doc-ok' : 'mc-doc-warn'}`;
                        cell.appendChild(badge);
                    }
                }
                grid.appendChild(cell);
            }
        }

        function esc(s) { return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }

        function showDetail(dateKey) {
            const evs = eventsByDate[dateKey] || [];
            if (!evs.length) return;

            const p = dateKey.split('-');
            const dateLabel = `${p[2]}/${p[1]}/${p[0]}`;
            detTitle.innerHTML = `<i class="bi bi-calendar-event me-1"></i> Chi tiết ngày ${esc(dateLabel)}`;

            detBody.innerHTML = evs.map(ev => {
                const docChip = ev.DOC_REQUIRED
                    ? `<span class="mc-doc-chip ${ev.DOC_STATUS === 'SUBMITTED' ? 'ok' : 'warn'}">
                           ${ev.DOC_STATUS === 'SUBMITTED' ? '✓' : '❗'} ${esc(DOC_STATUS_LABEL[ev.DOC_STATUS] || DOC_STATUS_DEFAULT)}
                       </span>`
                    : '';
                // Tên người duyệt/người sắp lịch đọc theo font VNI-Windows (CNAME) — phải bọc riêng
                // bằng class vni-font, không lẫn chung với chữ Unicode thường của dòng Lý do.
                const signerLine = ev.SIGNER_NAME
                    ? `<div class="mc-det">${esc(ev.SIGNER_LABEL || '')}: <span class="vni-font">${esc(ev.SIGNER_NAME)}</span></div>`
                    : '';
                const attMissingAttr = ev.TYPE === 'ATT_MISSING' ? ` data-att-date="${esc(dateKey)}"` : '';
                return `
                <div class="mc-event mc-${esc(ev.TYPE)}"${attMissingAttr}>
                    <div class="mc-icon">${iconFor(ev.TYPE)}</div>
                    <div class="mc-body">
                        <div class="mc-lbl">${esc(ev.LABEL)}</div>
                        ${signerLine}
                        ${ev.DETAIL ? `<div class="mc-det">${esc(ev.DETAIL)}</div>` : ''}
                        ${docChip}
                    </div>
                </div>`;
            }).join('') + `
                <div class="mc-footer-note">
                    <i class="bi bi-info-circle"></i>
                    Chỉ hiện đơn đã làm trên app — đơn viết giấy không hiện ở đây
                </div>`;

            if (window.bootstrap && modalEl) {
                new bootstrap.Modal(modalEl).show();
            }
        }

        // Click vào dòng "thiếu chấm công" trong popup chi tiết -> mở form khai giờ vào/ra.
        if (detBody) {
            detBody.addEventListener('click', (e) => {
                const item = e.target.closest('[data-att-date]');
                if (!item) return;
                window.location.href = `${rootUrl}AttendanceConfirm/WorkerForm?date=${item.dataset.attDate}`;
            });
        }

        // Ngày 10 mỗi tháng = ngày lãnh lương — quy tắc lịch cố định, không phải dữ liệu cá nhân
        // nên trang trí thẳng ở client, không cần chờ/phụ thuộc API (vẫn hiện dù API lỗi).
        function addPaydayDecoration() {
            const key = `${curY}-${pad(curM)}-10`;
            if (!eventsByDate[key]) eventsByDate[key] = [];
            eventsByDate[key].push({ TYPE: 'PAYDAY', LABEL: 'Ngày lãnh lương', DETAIL: 'Chúc mừng ngày lương! 💰' });
        }

        async function loadMonth() {
            eventsByDate = {};
            try {
                const url = `${rootUrl}Home/MyCalendar?year=${curY}&month=${curM}`;
                const resp = await fetch(url, { headers: { 'Accept': 'application/json' } });
                const json = await resp.json();
                if (json && json.success && Array.isArray(json.data)) {
                    json.data.forEach(ev => {
                        if (!eventsByDate[ev.DATE]) eventsByDate[ev.DATE] = [];
                        eventsByDate[ev.DATE].push(ev);
                    });
                }
            } catch (e) { console.warn('[MyCalendar] load fail', e); }
            addPaydayDecoration();
            renderGrid();
        }

        // Navigation
        root.addEventListener('click', (e) => {
            const nav = e.target.closest('.mycal-nav');
            if (nav) {
                const dir = nav.dataset.nav === 'next' ? 1 : -1;
                curM += dir;
                if (curM > 12) { curM = 1; curY++; }
                if (curM < 1)  { curM = 12; curY--; }
                loadMonth();
                return;
            }
            const cell = e.target.closest('.mycal-cell.mycal-has-event');
            if (cell && cell.dataset.date) showDetail(cell.dataset.date);
        });

        loadMonth();
    })();

})();
