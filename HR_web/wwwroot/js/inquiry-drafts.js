// Bản nháp trả lời HR/GA chờ CSR duyệt (yêu cầu 2026-11-06) — dùng trong trang HrInquiry/Chat.
//  • HR/GA: thấy bản nháp của phòng mình (Chờ duyệt / Bị trả lại kèm góp ý), thu hồi hoặc "dùng lại để sửa".
//  • CSR: Duyệt & gửi nguyên văn · Sửa trước khi gửi (nạp vào khung soạn, bấm Gửi để duyệt bản đã sửa) · Trả lại kèm góp ý.
window.InquiryDrafts = (function () {
    let cfg = null, editingId = 0, timer = null, lastHtml = null, busy = false;
    const $ = id => document.getElementById(id);
    const esc = s => String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    const safe = h => (window.InquiryChat && InquiryChat.safeHtml) ? InquiryChat.safeHtml(h) : esc(h);
    const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    const toast = (m, t) => { if (window.showToast) window.showToast(m, t); };
    const fmt = d => { const x = new Date(d); return isNaN(x) ? '' : x.toLocaleString('vi-VN', { hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit' }); };

    async function post(url, body) {
        const res = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() }, body: JSON.stringify(body) });
        return res.json();
    }

    function card(d, isCsr) {
        const pending = d.status === 'PENDING';
        const files = (d.files || []).map(f => `<span class="dr-chip"><span class="material-symbols-rounded">attach_file</span>${esc(f.fileName)}</span>`).join('');
        const refs = d.refCount > 0 ? `<span class="dr-chip"><span class="material-symbols-rounded">menu_book</span>${d.refCount} trích dẫn</span>` : '';
        let actions = '';
        if (pending && isCsr)
            actions += `<button class="dr-btn ok" onclick="InquiryDrafts.approve(${d.id})">Duyệt &amp; gửi</button>
                        <button class="dr-btn" onclick="InquiryDrafts.edit(${d.id})">Sửa trước khi gửi</button>
                        <button class="dr-btn warn" onclick="InquiryDrafts.openReturn(${d.id})">Trả lại</button>`;
        if (pending && d.mine) actions += `<button class="dr-btn" onclick="InquiryDrafts.dismiss(${d.id})">Thu hồi</button>`;
        if (!pending && d.mine)
            actions += `<button class="dr-btn ok" onclick="InquiryDrafts.reuse(${d.id})">Dùng lại để sửa</button>
                        <button class="dr-btn" onclick="InquiryDrafts.dismiss(${d.id})">Bỏ</button>`;
        const note = !pending && d.reviewNote
            ? `<div class="dr-note"><b>CSR <span class="vni-font">${esc(d.reviewerName || '')}</span> góp ý:</b> ${esc(d.reviewNote)}</div>` : '';
        return `<div class="dr-card ${pending ? 'pending' : 'returned'}" data-id="${d.id}">
            <div class="dr-head">
                <span class="dr-badge ${pending ? 'p' : 'r'}">${pending ? 'Chờ CSR duyệt' : 'Bị trả lại'}</span>
                <span class="dr-by">Bản nháp của <b class="vni-font">${esc(d.authorName || '')}</b>${d.authorRole ? ` (${esc(d.authorRole)})` : ''}${d.mine ? ' — bạn' : ''}</span>
                <span class="dr-time">${fmt(d.createdDt)}</span>
            </div>
            ${d.content ? `<div class="dr-body msg-content">${safe(d.content)}</div>` : ''}
            ${(files || refs) ? `<div class="dr-chips">${files}${refs}</div>` : ''}
            ${note}
            <div class="dr-return" id="drRet${d.id}" style="display:none">
                <textarea id="drNote${d.id}" rows="2" maxlength="1000" placeholder="Góp ý cho người soạn (bắt buộc)"></textarea>
                <button class="dr-btn warn" onclick="InquiryDrafts.doReturn(${d.id})">Xác nhận trả lại</button>
                <button class="dr-btn" onclick="InquiryDrafts.openReturn(${d.id}, true)">Hủy</button>
            </div>
            ${actions ? `<div class="dr-actions">${actions}</div>` : ''}
        </div>`;
    }

    let cache = [];
    function render(data) {
        const box = $('draftPanel'); if (!box) return;
        cache = data.drafts || [];
        // Bản nháp đang sửa đã bị xử lý (người soạn thu hồi / CSR khác duyệt) → thôi chế độ sửa, giữ nguyên chữ trong khung soạn
        if (editingId && !cache.some(x => x.id === editingId)) {
            editingId = 0;
            const b = $('draftEditBanner'); if (b) b.style.display = 'none';
            toast('Bản nháp đang sửa đã được xử lý ở nơi khác — nội dung vẫn còn trong khung soạn', 'warning');
        }
        const html = cache.length
            ? `<div class="dr-title"><span class="material-symbols-rounded">edit_note</span>Bản nháp trả lời (${cache.length})</div>` + cache.map(d => card(d, data.isCsr)).join('')
            : '';
        // Không vẽ lại khi dữ liệu không đổi → không làm mất chữ đang gõ trong ô góp ý
        const key = JSON.stringify(cache);
        if (key === lastHtml) return;
        const focusedNote = document.activeElement && document.activeElement.id && document.activeElement.id.startsWith('drNote');
        if (focusedNote) return;
        lastHtml = key;
        box.innerHTML = html;
        box.style.display = cache.length ? '' : 'none';
    }

    async function refresh() {
        if (!cfg || busy) return;
        try {
            const res = await fetch(`${cfg.urls.list}?id=${cfg.inquiryId}`);
            const data = await res.json();
            if (data.success) render(data);
        } catch { /* mất mạng tạm thời: lần poll sau */ }
    }

    function init(c) {
        cfg = c; refresh();
        timer = setInterval(() => { if (!document.hidden) refresh(); }, 8000);
    }

    async function approve(id) {
        const go = async () => {
            busy = true;
            try {
                const d = await post(cfg.urls.approve, { id });
                if (!d.success) { toast(d.message || 'Không duyệt được', 'error'); }
                else { toast('Đã duyệt & gửi cho công nhân', 'success'); if (window.InquiryChat) await InquiryChat.refreshMessages(); }
            } catch { toast('Lỗi kết nối', 'error'); }
            busy = false; lastHtml = null; refresh();
        };
        if (window.showConfirm) showConfirm('Duyệt và gửi nguyên văn bản nháp này cho công nhân?', go, { title: 'Duyệt & gửi', okText: 'Gửi', okClass: 'btn-success', icon: 'send', iconColor: '#16a34a', iconBg: '#dcfce7' });
        else if (confirm('Duyệt và gửi bản nháp này?')) go();
    }

    function edit(id) {
        const d = cache.find(x => x.id === id);
        if (!d) return;
        if (!window.InquiryEditor) { toast('Ô soạn đang bị khóa (hội thoại có người khác phụ trách) — hãy "Mở khóa" để sửa, hoặc duyệt nguyên văn', 'error'); return; }
        window.InquiryEditor.setHTML(d.content || '');
        editingId = id;
        const b = $('draftEditBanner');
        if (b) { b.style.display = ''; b.querySelector('.who').textContent = d.authorName || ''; }
        window.InquiryEditor.focus && window.InquiryEditor.focus();
        $('draftEditBanner')?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    function cancelEdit() {
        editingId = 0;
        const b = $('draftEditBanner'); if (b) b.style.display = 'none';
        if (window.InquiryEditor) window.InquiryEditor.clear();
    }

    // Gọi sau khi gửi thành công bản đã sửa
    function done() {
        editingId = 0;
        const b = $('draftEditBanner'); if (b) b.style.display = 'none';
        lastHtml = null; refresh();
    }

    function openReturn(id, hide) {
        const el = $('drRet' + id); if (!el) return;
        el.style.display = hide ? 'none' : '';
        if (!hide) $('drNote' + id)?.focus();
    }

    async function doReturn(id) {
        const note = ($('drNote' + id)?.value || '').trim();
        if (!note) { toast('Vui lòng nhập góp ý', 'error'); return; }
        try {
            const d = await post(cfg.urls.ret, { id, note });
            if (!d.success) { toast(d.message || 'Không trả lại được', 'error'); return; }
            toast('Đã trả lại cho người soạn', 'success');
            if (editingId === id) cancelEdit();
        } catch { toast('Lỗi kết nối', 'error'); return; }
        lastHtml = null; refresh();
    }

    async function dismiss(id) {
        try {
            const d = await post(cfg.urls.dismiss, { id });
            if (!d.success) { toast(d.message || 'Không thu hồi được', 'error'); return; }
        } catch { toast('Lỗi kết nối', 'error'); return; }
        lastHtml = null; refresh();
    }

    // Người soạn nạp lại nội dung bị trả lại vào khung soạn để sửa rồi gửi lại, bản cũ được bỏ
    async function reuse(id) {
        const d = cache.find(x => x.id === id);
        if (!d || !window.InquiryEditor) { toast('Khung soạn chưa sẵn sàng', 'error'); return; }
        window.InquiryEditor.setHTML(d.content || '');
        await dismiss(id);
        window.InquiryEditor.focus && window.InquiryEditor.focus();
    }

    return { init, refresh, approve, edit, cancelEdit, done, openReturn, doReturn, dismiss, reuse, editingId: () => editingId };
})();
