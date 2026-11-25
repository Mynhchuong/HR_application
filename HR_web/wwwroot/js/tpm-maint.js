// Nút xử lý phiếu báo hư dùng chung cho 2 trang bảo trì (Tpm/MachineInfo, Tpm/Maintenance) — LÀM GIỐNG PDA (BrokenHandlingCard):
//   Chờ nhận:      [Nhận xử lý] [Hủy báo hư]
//   Đã nhận:       [Sửa xong] [Cần linh kiện] [Hủy báo hư]
//   Chờ linh kiện: (hiện ghi chú linh kiện) [Sửa xong] [Hủy báo hư]
// "Sửa xong" 1 chạm (mã máy lấy từ máy đang xem, không bắt quét lại — như PDA). Không có form loại lỗi/cách xử lý.
window.TpmMaint = (function () {
    const NO_PERM = 'Tài khoản của bạn chưa có quyền bảo trì trên hệ thống TPM. Vui lòng liên hệ quản lý bảo trì.';
    const ERR = {
        MYSAMHO_NOT_MAINTENANCE: NO_PERM,
        MYSAMHO_MAINTENANCE_FORBIDDEN: NO_PERM,
        BROKEN_CONFIRM_MISMATCH: 'Mã máy không khớp với phiếu này.',
        BROKEN_INVALID_TRANSITION: 'Phiếu đã được xử lý bởi người khác hoặc không còn ở trạng thái này. Hãy tải lại.',
        BROKEN_NOT_FOUND: 'Không tìm thấy phiếu (có thể đã bị hủy). Hãy tải lại.',
        MISSING_PARTS_NOTE: 'Vui lòng ghi rõ cần linh kiện gì.'
    };
    const STATUS = {
        REPORTED:    ['Chờ nhận',      '#fff7ed', '#9a3412'],
        RECEIVED:    ['Đã nhận',       '#eff6ff', '#1d4ed8'],
        NEEDS_PARTS: ['Chờ linh kiện', '#fefce8', '#a16207'],
        REPAIRED:    ['Đã sửa xong',   '#f0fdf4', '#166534'],
        CANCELLED:   ['Đã hủy',        '#f1f5f9', '#475569']
    };
    let cfg = { urls: {}, token: '', onChanged: () => {} };

    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const toast = (m, t) => (window.showToast ? window.showToast(m, t) : alert(m));
    const errText = d => ERR[d.code] || d.message || 'Thao tác thất bại.';

    function init(c) { cfg = Object.assign(cfg, c); }
    const badge = st => { const x = STATUS[st] || [st, '#f1f5f9', '#475569']; return `<span class="tpm-badge" style="background:${x[1]};color:${x[2]}">${esc(x[0])}</span>`; };

    function actionsHtml(b, itemCode) {
        const id = b.BrokenId, code = esc(itemCode);
        const btn = (act, label, cls) => `<button type="button" class="tpm-btn ${cls || 'primary'} mt-2" data-tm="${act}" data-id="${id}" data-code="${code}">${label}</button>`;
        let h = '';
        if (b.Status === 'REPORTED') h = btn('recv', 'Nhận xử lý') + btn('cancel', 'Hủy báo hư', 'light');
        else if (b.Status === 'RECEIVED') h = btn('repair', 'Sửa xong') + btn('parts', 'Cần linh kiện', 'light') + btn('cancel', 'Hủy báo hư', 'light');
        else if (b.Status === 'NEEDS_PARTS')
            h = (b.PartsNote ? `<div class="tpm-notice mt-2 mb-0">Cần linh kiện: <b>${esc(b.PartsNote)}</b></div>` : '')
              + btn('repair', 'Sửa xong') + btn('cancel', 'Hủy báo hư', 'light');
        return `<div class="tm-actions" id="tm${id}">${h}</div>`;
    }

    async function call(btn, act, body, okMsg) {
        const u = cfg.urls[act];
        btn.disabled = true;
        try {
            const r = await fetch(u, { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': cfg.token }, body: JSON.stringify(body) });
            const d = await r.json();
            if (!d.success) { btn.disabled = false; toast(errText(d), 'error'); return; }
            toast(okMsg, 'success'); cfg.onChanged();
        } catch (e) { btn.disabled = false; toast('Lỗi kết nối', 'error'); }
    }

    function showPartsForm(box, id) {
        box.dataset.prev = box.innerHTML;
        box.innerHTML = `<textarea class="tpm-input mt-2" id="tmNote${id}" maxlength="200" placeholder="Cần linh kiện gì? (VD: dây curoa, mô tơ...)"></textarea>
            <button type="button" class="tpm-btn primary mt-1" data-tm="partsSend" data-id="${id}">Gửi</button>
            <button type="button" class="tpm-btn light mt-2" data-tm="partsBack" data-id="${id}">Quay lại</button>`;
    }

    function bind(root) {
        root.addEventListener('click', e => {
            const b = e.target.closest('[data-tm]'); if (!b) return;
            const act = b.dataset.tm, id = parseInt(b.dataset.id, 10), code = b.dataset.code;
            const box = document.getElementById('tm' + id);
            if (act === 'recv')   { if (confirm('Nhận phiếu này để xử lý?')) call(b, 'recv', { brokenId: id }, 'Đã nhận phiếu'); }
            else if (act === 'repair') { if (confirm('Xác nhận máy ' + code + ' đã sửa xong?')) call(b, 'repair', { brokenId: id, itemCode: code }, 'Đã ghi nhận sửa xong'); }
            else if (act === 'cancel') { if (confirm('Hủy phiếu báo hư này?')) call(b, 'cancel', { brokenId: id }, 'Đã hủy phiếu'); }
            else if (act === 'parts')  { showPartsForm(box, id); }
            else if (act === 'partsBack') { box.innerHTML = box.dataset.prev || ''; }
            else if (act === 'partsSend') {
                const note = (document.getElementById('tmNote' + id).value || '').trim();
                if (!note) { toast('Vui lòng ghi rõ cần linh kiện gì', 'error'); return; }
                call(b, 'parts', { brokenId: id, partsNote: note }, 'Đã báo cần linh kiện');
            }
        });
    }

    return { init, bind, actionsHtml, badge, esc, errText, toast };
})();
