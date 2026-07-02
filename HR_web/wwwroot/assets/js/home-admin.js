// =============================================================
// HomeAdmin — Banner/Greeting/Rule/Audit
// =============================================================
(function () {
    'use strict';

    const $  = (sel, root) => (root || document).querySelector(sel);
    const $$ = (sel, root) => Array.from((root || document).querySelectorAll(sel));

    const ctx = window.__HR_HOME_ADMIN__ || { isAdmin: false };
    const ROOT = ((ctx.rootUrl) || '/').replace(/\/+$/, '') + '/';
    const csrfToken = () => (document.querySelector('input[name="__RequestVerificationToken"]') || {}).value || '';

    // ─── Utility ────────────────────────────────────────────────
    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
    }
    async function postJson(url, body) {
        try {
            const res = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': csrfToken()
                },
                credentials: 'same-origin',
                body: JSON.stringify(body || {})
            });
            if (res.status === 401 || res.status === 403) {
                return { success: false, message: 'Bạn không có quyền hoặc phiên hết hạn — vui lòng đăng nhập lại' };
            }
            if (!res.ok) {
                return { success: false, message: 'Lỗi máy chủ (' + res.status + ')' };
            }
            return await res.json();
        } catch (e) {
            return { success: false, message: 'Lỗi kết nối: ' + (e.message || 'unknown') };
        }
    }
    async function getJson(url) {
        try {
            const res = await fetch(url, {
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                credentials: 'same-origin'
            });
            if (res.status === 401 || res.status === 403) {
                return { success: false, message: 'Bạn không có quyền hoặc phiên hết hạn' };
            }
            if (!res.ok) {
                return { success: false, message: 'Lỗi máy chủ (' + res.status + ')' };
            }
            return await res.json();
        } catch (e) {
            return { success: false, message: 'Lỗi kết nối: ' + (e.message || 'unknown') };
        }
    }
    function fmtDt(iso) {
        if (!iso) return '—';
        try { return new Date(iso).toLocaleString('vi-VN'); } catch { return iso; }
    }
    function fmtDate(iso) {
        if (!iso) return '—';
        try { return new Date(iso).toLocaleDateString('vi-VN'); } catch { return iso; }
    }
    function toDatetimeLocalString(iso) {
        try {
            const d = new Date(iso);
            const pad = n => String(n).padStart(2, '0');
            return `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
        } catch { return ''; }
    }

    // ─── Modal helpers ──────────────────────────────────────────
    document.addEventListener('click', (e) => {
        const closer = e.target.closest('[data-close]');
        if (!closer) return;
        const which = closer.dataset.close;
        const map = { banner: '#bannerModal', greeting: '#greetingModal', audit: '#auditDetailModal' };
        const m = $(map[which]);
        if (m) m.hidden = true;
    });
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            $$('.admin-modal').forEach(m => m.hidden = true);
        }
    });

    // ═════════════════════════════════════════════════════════════
    // BANNER
    // ═════════════════════════════════════════════════════════════
    (function initBanner() {
        const listEl = $('#bannerList');
        if (!listEl) return;

        async function loadList() {
            listEl.innerHTML = '<div class="admin-loading">Đang tải…</div>';
            try {
                const json = await getJson(ROOT + 'HomeAdmin/BannerList');
                if (!json.success) throw new Error(json.message || 'load fail');
                renderList(json.data || []);
            } catch (e) {
                listEl.innerHTML = `<div class="admin-loading">Lỗi: ${escapeHtml(e.message)}</div>`;
            }
        }

        function statusBadge(b) {
            const now = new Date();
            const from = new Date(b.PUBLISH_FROM);
            const to   = new Date(b.PUBLISH_TO);
            if (!b.IS_ACTIVE) return '<span class="admin-banner-status admin-banner-status-inactive">Đã xoá</span>';
            if (now < from)  return '<span class="admin-banner-status admin-banner-status-scheduled">Sắp hiện</span>';
            if (now > to)    return '<span class="admin-banner-status admin-banner-status-expired">Hết hạn</span>';
            return '<span class="admin-banner-status admin-banner-status-active">Đang active</span>';
        }

        function renderList(items) {
            if (items.length === 0) {
                listEl.innerHTML = '<div class="admin-loading">Chưa có banner nào.</div>';
                return;
            }
            listEl.innerHTML = items.map(b => `
                <div class="admin-banner-card">
                    <div class="admin-banner-card-thumb">
                        <img src="${ROOT}Image/GetHomeBanner?fileName=${encodeURIComponent(b.IMAGE_FILE)}" alt="" onerror="this.style.display='none'" />
                    </div>
                    <div class="admin-banner-card-body">
                        <div class="admin-banner-card-title">${statusBadge(b)} #${b.ID}</div>
                        <div class="admin-banner-card-meta">
                            📅 ${fmtDate(b.PUBLISH_FROM)} → ${fmtDate(b.PUBLISH_TO)}<br />
                            ${b.OVERLAY_TEXT_VI ? '🇻🇳 ' + escapeHtml(b.OVERLAY_TEXT_VI) + '<br />' : ''}
                            ${b.OVERLAY_TEXT_EN ? '🇬🇧 ' + escapeHtml(b.OVERLAY_TEXT_EN) + '<br />' : ''}
                            ${b.LINK_URL ? '🔗 ' + escapeHtml(b.LINK_URL) + '<br />' : ''}
                            ${b.TARGET_ROLES ? '👥 ' + escapeHtml(b.TARGET_ROLES) : ''}
                        </div>
                    </div>
                    <div class="admin-banner-card-actions">
                        <button type="button" class="btn btn-secondary" data-edit-banner="${b.ID}">Sửa</button>
                        ${ctx.isAdmin && b.IS_ACTIVE ? `<button type="button" class="btn btn-danger" data-delete-banner="${b.ID}">Xoá</button>` : ''}
                    </div>
                </div>
            `).join('');
        }

        // Actions
        document.addEventListener('click', async (e) => {
            const newBtn = e.target.closest('[data-action="new-banner"]');
            if (newBtn) { openModal(null); return; }
            const editBtn = e.target.closest('[data-edit-banner]');
            if (editBtn) { openModal(parseInt(editBtn.dataset.editBanner, 10)); return; }
            const delBtn = e.target.closest('[data-delete-banner]');
            if (delBtn) { deleteBanner(parseInt(delBtn.dataset.deleteBanner, 10)); return; }
        });

        async function deleteBanner(id) {
            if (!confirm('Xoá banner #' + id + '?')) return;
            const json = await postJson(ROOT + 'HomeAdmin/BannerDelete', { Id: id });
            if (json.success) { loadList(); }
            else alert(json.message || 'Lỗi xoá');
        }

        // Modal
        const modal = $('#bannerModal');
        let editing = null;
        function openModal(id) {
            editing = null;
            resetForm();
            $('#bannerModalTitle').textContent = id ? 'Sửa banner #' + id : 'Thêm banner';
            if (id) {
                getJson(ROOT + 'HomeAdmin/BannerList').then(json => {
                    const b = (json.data || []).find(x => x.ID === id);
                    if (b) {
                        editing = b;
                        fillForm(b);
                    }
                });
            }
            modal.hidden = false;
        }

        function resetForm() {
            $('#bannerForm').reset();
            $('#bannerId').value = '';
            $('#bannerImageFile').value = '';
            $('#bannerPreview').innerHTML = '<div class="admin-upload-placeholder">Chưa có ảnh</div>';
            $('#bannerUploadHint').textContent = 'Chọn ảnh 1920×1080. Server sẽ kiểm tra ratio 16:9 và resize.';
            $('#bannerUploadHint').className = 'admin-upload-hint';
            $('#bannerFormError').hidden = true;
        }

        function fillForm(b) {
            $('#bannerId').value              = b.ID;
            $('#bannerImageFile').value       = b.IMAGE_FILE || '';
            $('#bannerPublishFrom').value     = toDatetimeLocalString(b.PUBLISH_FROM);
            $('#bannerPublishTo').value       = toDatetimeLocalString(b.PUBLISH_TO);
            $('#bannerOverlayVi').value       = b.OVERLAY_TEXT_VI || '';
            $('#bannerOverlayEn').value       = b.OVERLAY_TEXT_EN || '';
            $('#bannerOverlayPos').value      = b.OVERLAY_POS || 'BL';
            $('#bannerLinkUrl').value         = b.LINK_URL || '';
            $('#bannerLinkTarget').value      = b.LINK_TARGET || '_self';
            $('#bannerTargetRoles').value     = b.TARGET_ROLES || '';
            $('#bannerDismissible').checked   = b.IS_DISMISSIBLE;

            if (b.IMAGE_FILE) {
                $('#bannerPreview').innerHTML = `<img src="${ROOT}Image/GetHomeBanner?fileName=${encodeURIComponent(b.IMAGE_FILE)}" />`;
                $('#bannerUploadHint').textContent = '✅ Ảnh hiện tại: ' + b.IMAGE_FILE;
                $('#bannerUploadHint').className = 'admin-upload-hint text-success';
            }
        }

        // File upload — validate + upload ngay
        const fileInput = $('#bannerFileInput');
        if (fileInput) fileInput.addEventListener('change', async (e) => {
            const file = e.target.files[0];
            if (!file) return;
            const hint = $('#bannerUploadHint');

            // Client-side validate
            const r = await validateBannerFile(file);
            if (!r.ok) {
                hint.textContent = r.msg;
                hint.className = 'admin-upload-hint text-danger';
                return;
            }
            hint.textContent = r.info + ' — đang upload…';
            hint.className = 'admin-upload-hint text-success';

            // Upload
            const fd = new FormData();
            fd.append('file', file);
            let json;
            try {
                const res = await fetch(ROOT + 'Image/UploadHomeBanner', {
                    method: 'POST',
                    body: fd,
                    credentials: 'same-origin',
                    headers: { 'RequestVerificationToken': csrfToken() }
                });
                if (!res.ok) {
                    json = { success: false, message: 'Upload failed (' + res.status + ')' };
                } else {
                    json = await res.json();
                }
            } catch (err) {
                json = { success: false, message: 'Lỗi kết nối: ' + err.message };
            }
            if (json.success) {
                $('#bannerImageFile').value = json.fileName;
                $('#bannerPreview').innerHTML = `<img src="${ROOT}Image/GetHomeBanner?fileName=${encodeURIComponent(json.fileName)}" />`;
                hint.textContent = '✅ Upload thành công: ' + json.fileName;
                hint.className = 'admin-upload-hint text-success';
            } else {
                hint.textContent = '❌ ' + (json.message || 'Upload lỗi');
                hint.className = 'admin-upload-hint text-danger';
            }
        });

        async function validateBannerFile(file) {
            const ALLOWED = ['image/jpeg', 'image/png', 'image/webp'];
            if (!ALLOWED.includes(file.type))
                return { ok: false, msg: 'Chỉ chấp nhận JPG/PNG/WebP' };
            if (file.size > 5 * 1024 * 1024)
                return { ok: false, msg: `Ảnh ${(file.size / 1024 / 1024).toFixed(1)}MB, tối đa 5MB` };

            const dim = await new Promise((resolve, reject) => {
                const img = new Image();
                img.onload  = () => resolve({ w: img.naturalWidth, h: img.naturalHeight });
                img.onerror = () => reject(new Error('Không đọc được ảnh'));
                img.src = URL.createObjectURL(file);
            });
            if (dim.w < 1200 || dim.h < 675)
                return { ok: false, msg: `Ảnh ${dim.w}×${dim.h} quá nhỏ (min 1200×675)` };
            if (dim.w > 4096 || dim.h > 2304)
                return { ok: false, msg: `Ảnh ${dim.w}×${dim.h} quá lớn (max 4096×2304)` };

            const ratio       = dim.w / dim.h;
            const targetRatio = 16 / 9;
            if (Math.abs(ratio - targetRatio) > 0.02 * targetRatio)
                return { ok: false, msg: `Tỉ lệ sai (${dim.w}×${dim.h} = ${ratio.toFixed(2)}). Cần 16:9 — crop 1920×1080` };

            return {
                ok: true,
                info: dim.w === 1920 && dim.h === 1080
                    ? '✅ Kích thước chuẩn 1920×1080'
                    : `✅ Tỉ lệ 16:9 OK, server sẽ resize (${dim.w}×${dim.h})`
            };
        }

        // Submit
        const form = $('#bannerForm');
        if (form) form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const err = $('#bannerFormError'); err.hidden = true;
            const imageFile = $('#bannerImageFile').value;
            if (!imageFile) { err.textContent = 'Vui lòng upload ảnh trước khi lưu'; err.hidden = false; return; }

            const payload = {
                Id:             $('#bannerId').value ? parseInt($('#bannerId').value, 10) : null,
                ImageFile:      imageFile,
                OverlayTextVi:  $('#bannerOverlayVi').value.trim() || null,
                OverlayTextEn:  $('#bannerOverlayEn').value.trim() || null,
                OverlayPos:     $('#bannerOverlayPos').value,
                LinkUrl:        $('#bannerLinkUrl').value.trim() || null,
                LinkTarget:     $('#bannerLinkTarget').value,
                TargetRoles:    $('#bannerTargetRoles').value.trim() || null,
                IsDismissible:  $('#bannerDismissible').checked,
                PublishFrom:    $('#bannerPublishFrom').value,
                PublishTo:      $('#bannerPublishTo').value
            };
            const btn = $('#bannerSaveBtn');
            btn.disabled = true;
            try {
                const json = await postJson(ROOT + 'HomeAdmin/BannerSave', payload);
                if (json.success) {
                    modal.hidden = true;
                    loadList();
                } else {
                    err.textContent = json.message || 'Lỗi lưu';
                    err.hidden = false;
                }
            } finally { btn.disabled = false; }
        });

        loadList();
    })();

    // ═════════════════════════════════════════════════════════════
    // GREETING + RULE
    // ═════════════════════════════════════════════════════════════
    (function initGreeting() {
        const listEl = $('#greetingList');
        if (!listEl) return;

        let currentSlot = 'MORNING';

        async function loadList(slot) {
            currentSlot = slot;
            listEl.innerHTML = '<div class="admin-loading">Đang tải…</div>';
            try {
                const json = await getJson(ROOT + 'HomeAdmin/GreetingList?slot=' + encodeURIComponent(slot));
                if (!json.success) throw new Error(json.message || 'load fail');
                renderList(json.data || []);
            } catch (e) {
                listEl.innerHTML = `<div class="admin-loading">Lỗi: ${escapeHtml(e.message)}</div>`;
            }
        }

        function renderList(items) {
            if (items.length === 0) {
                listEl.innerHTML = '<div class="admin-loading">Chưa có câu chào cho slot này.</div>';
                return;
            }
            listEl.innerHTML = items.map(g => `
                <div class="admin-greeting-row ${g.IS_ACTIVE ? '' : 'inactive'}">
                    <div class="admin-greeting-emoji">${g.EMOJI ? escapeHtml(g.EMOJI) : '💬'}</div>
                    <div class="admin-greeting-content">
                        <div class="admin-greeting-vi">${escapeHtml(g.MESSAGE_VI)}</div>
                        ${g.MESSAGE_EN ? '<div class="admin-greeting-en">🇬🇧 ' + escapeHtml(g.MESSAGE_EN) + '</div>' : ''}
                    </div>
                    <div class="admin-greeting-actions">
                        <button type="button" class="btn btn-secondary" data-edit-greeting="${g.ID}">Sửa</button>
                        <button type="button" class="btn btn-danger" data-delete-greeting="${g.ID}">Xoá</button>
                    </div>
                </div>
            `).join('');
        }

        // Tab switching
        $$('.admin-greeting-tab').forEach(btn => btn.addEventListener('click', () => {
            $$('.admin-greeting-tab').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            loadList(btn.dataset.slot);
        }));

        // CRUD
        document.addEventListener('click', async (e) => {
            const newBtn = e.target.closest('[data-action="new-greeting"]');
            if (newBtn) { openModal(null); return; }
            const editBtn = e.target.closest('[data-edit-greeting]');
            if (editBtn) { openModal(parseInt(editBtn.dataset.editGreeting, 10)); return; }
            const delBtn = e.target.closest('[data-delete-greeting]');
            if (delBtn) { deleteItem(parseInt(delBtn.dataset.deleteGreeting, 10)); return; }
        });

        async function deleteItem(id) {
            if (!confirm('Xoá câu chào #' + id + '?')) return;
            const json = await postJson(ROOT + 'HomeAdmin/GreetingDelete', { Id: id });
            if (json.success) loadList(currentSlot);
            else alert(json.message || 'Lỗi');
        }

        const modal = $('#greetingModal');
        function openModal(id) {
            const form = $('#greetingForm');
            form.reset();
            $('#greetingId').value = '';
            $('#greetingFormError').hidden = true;
            $('#greetingSlot').value = currentSlot;
            $('#greetingActive').checked = true;
            $('#greetingModalTitle').textContent = id ? 'Sửa câu chào #' + id : 'Thêm câu chào';
            if (id) {
                getJson(ROOT + 'HomeAdmin/GreetingList').then(json => {
                    const g = (json.data || []).find(x => x.ID === id);
                    if (g) {
                        $('#greetingId').value       = g.ID;
                        $('#greetingSlot').value     = g.TIME_SLOT;
                        $('#greetingVi').value       = g.MESSAGE_VI;
                        $('#greetingEn').value       = g.MESSAGE_EN || '';
                        $('#greetingEmoji').value    = g.EMOJI || '';
                        $('#greetingActive').checked = g.IS_ACTIVE;
                    }
                });
            }
            modal.hidden = false;
        }

        $('#greetingForm').addEventListener('submit', async (e) => {
            e.preventDefault();
            const err = $('#greetingFormError'); err.hidden = true;
            const payload = {
                Id:        $('#greetingId').value ? parseInt($('#greetingId').value, 10) : null,
                TimeSlot:  $('#greetingSlot').value,
                MessageVi: $('#greetingVi').value.trim(),
                MessageEn: $('#greetingEn').value.trim() || null,
                Emoji:     $('#greetingEmoji').value.trim() || null,
                IsActive:  $('#greetingActive').checked
            };
            const json = await postJson(ROOT + 'HomeAdmin/GreetingSave', payload);
            if (json.success) {
                modal.hidden = true;
                loadList(currentSlot);
            } else {
                err.textContent = json.message || 'Lỗi';
                err.hidden = false;
            }
        });

        loadList('MORNING');

        // Rule section (only Admin)
        const ruleEl = $('#ruleList');
        if (ruleEl && ctx.isAdmin) loadRules();

        async function loadRules() {
            try {
                const json = await getJson(ROOT + 'HomeAdmin/RuleList');
                if (!json.success) throw new Error(json.message);
                renderRules(json.data || []);
            } catch (e) {
                ruleEl.innerHTML = `<div class="admin-loading">Lỗi: ${escapeHtml(e.message)}</div>`;
            }
        }

        function renderRules(items) {
            ruleEl.innerHTML = items.map(r => `
                <div class="admin-rule-row" data-slot="${r.TIME_SLOT}">
                    <div class="admin-rule-slot">${r.TIME_SLOT}</div>
                    <div class="admin-rule-input">
                        <input type="number" min="0" max="23" value="${r.FROM_HOUR}" data-field="from" />
                        <span class="sep">→</span>
                        <input type="number" min="0" max="23" value="${r.TO_HOUR}" data-field="to" />
                    </div>
                    <button type="button" class="btn btn-primary" data-save-rule="${r.TIME_SLOT}">Lưu</button>
                </div>
            `).join('');
        }

        document.addEventListener('click', async (e) => {
            const btn = e.target.closest('[data-save-rule]');
            if (!btn) return;
            const row = btn.closest('[data-slot]');
            const from = parseInt(row.querySelector('[data-field="from"]').value, 10);
            const to   = parseInt(row.querySelector('[data-field="to"]').value, 10);
            const payload = { TimeSlot: btn.dataset.saveRule, FromHour: from, ToHour: to };
            const json = await postJson(ROOT + 'HomeAdmin/RuleSave', payload);
            if (json.success) {
                btn.textContent = '✓ Đã lưu';
                setTimeout(() => btn.textContent = 'Lưu', 1500);
            } else alert(json.message || 'Lỗi');
        });
    })();

    // ═════════════════════════════════════════════════════════════
    // AUDIT
    // ═════════════════════════════════════════════════════════════
    (function initAudit() {
        const listEl = $('#auditList');
        if (!listEl) return;

        let currentPage = 1;
        let cachedItems = []; // cache items của trang hiện tại → showDetail không phải fetch lại

        async function load(page) {
            currentPage = page;
            listEl.innerHTML = '<div class="admin-loading">Đang tải…</div>';
            const params = new URLSearchParams({
                page: page,
                pageSize: 30,
                entity:   $('#auditEntity').value || '',
                actor:    $('#auditActor').value.trim() || '',
                dateFrom: $('#auditDateFrom').value || '',
                dateTo:   $('#auditDateTo').value || ''
            });
            try {
                const json = await getJson(ROOT + 'HomeAdmin/AuditList?' + params.toString());
                if (!json.success) throw new Error(json.message);
                cachedItems = (json.data && json.data.DATA) || [];
                render(json.data);
            } catch (e) {
                listEl.innerHTML = `<div class="admin-loading">Lỗi: ${escapeHtml(e.message)}</div>`;
            }
        }

        function render(page) {
            const items = page.DATA || [];
            if (items.length === 0) {
                listEl.innerHTML = '<div class="admin-loading">Không có bản ghi nào.</div>';
                $('#auditPager').innerHTML = '';
                return;
            }
            listEl.innerHTML = items.map(a => `
                <div class="admin-audit-row" data-audit-id="${a.ID}">
                    <div>
                        <span class="admin-audit-badge admin-audit-badge-${a.ACTION}">${a.ACTION}</span>
                        <span class="admin-audit-entity">${a.ENTITY} #${escapeHtml(a.ENTITY_ID || '')}</span>
                    </div>
                    <div>
                        <div class="admin-audit-actor"><span class="vni-font">${escapeHtml(a.ACTOR_NAME || a.ACTOR_EMPCD)}</span> <small>(${escapeHtml(a.ROLE_NAME || '')})</small></div>
                        <div class="admin-audit-time">${fmtDt(a.INST_DT)}</div>
                    </div>
                    <div style="text-align:right; color:#6366f1;">Xem →</div>
                </div>
            `).join('');
            renderPager(page);
        }

        function renderPager(page) {
            const total = page.TOTAL_PAGES || 1;
            const cur   = page.PAGE;
            const btns = [];
            btns.push(`<button ${cur === 1 ? 'disabled' : ''} data-page="${cur - 1}">‹ Trước</button>`);
            const start = Math.max(1, cur - 2), end = Math.min(total, cur + 2);
            for (let i = start; i <= end; i++)
                btns.push(`<button class="${i === cur ? 'active' : ''}" data-page="${i}">${i}</button>`);
            btns.push(`<button ${cur === total ? 'disabled' : ''} data-page="${cur + 1}">Sau ›</button>`);
            $('#auditPager').innerHTML = btns.join('');
        }

        document.addEventListener('click', (e) => {
            const btn = e.target.closest('#auditPager button[data-page]');
            if (btn && !btn.disabled) load(parseInt(btn.dataset.page, 10));
            const row = e.target.closest('[data-audit-id]');
            if (row) showDetail(parseInt(row.dataset.auditId, 10));
        });

        $('#auditFilterBtn').addEventListener('click', () => load(1));

        function showDetail(id) {
            const modal = $('#auditDetailModal');
            const body  = $('#auditDetailBody');
            const item = cachedItems.find(x => x.ID === id);
            if (!item) { body.innerHTML = 'Không tìm thấy'; return; }
            modal.hidden = false;
            body.innerHTML = `
                <div style="margin-bottom:12px;">
                    <strong>${item.ACTION}</strong> ${item.ENTITY} #${escapeHtml(item.ENTITY_ID || '')}
                    bởi <strong class="vni-font">${escapeHtml(item.ACTOR_NAME || item.ACTOR_EMPCD)}</strong>
                    (${escapeHtml(item.ROLE_NAME || '')}) — ${fmtDt(item.INST_DT)}
                </div>
                <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;">
                    <div>
                        <div class="form-label">OLD_VAL</div>
                        <pre style="background:#f9fafb;padding:10px;border-radius:8px;overflow:auto;font-size:0.8rem;max-height:400px;">${escapeHtml(prettyJson(item.OLD_VAL))}</pre>
                    </div>
                    <div>
                        <div class="form-label">NEW_VAL</div>
                        <pre style="background:#f9fafb;padding:10px;border-radius:8px;overflow:auto;font-size:0.8rem;max-height:400px;">${escapeHtml(prettyJson(item.NEW_VAL))}</pre>
                    </div>
                </div>
                <div style="margin-top:12px;font-size:0.8rem;color:#6b7280;">
                    IP: ${escapeHtml(item.IP_ADDRESS || '—')} · UA: ${escapeHtml((item.USER_AGENT || '').substring(0, 100))}
                </div>
            `;
        }

        function prettyJson(s) {
            if (!s) return '(null)';
            try { return JSON.stringify(JSON.parse(s), null, 2); }
            catch { return s; }
        }

        load(1);
    })();

})();
