/* ============================================================
   Inquiry Chat — shared client logic for AdminInquiry / EmployeeInquiry / HrInquiry
   Pair with inquiry-chat-editor.js (loaded as module) for the rich editor.
   Falls back to plain <textarea id="msgInput"> if InquiryEditor not present.
   ============================================================ */
window.InquiryChat = (function () {
    'use strict';

    const MAX_CHARS = 2000;

    let cfg = null;
    let lastMsgId = 0;
    let uploadedFiles = [];
    let selectedRefs  = [];   // [{ refType, refId, refTitle, category }]
    let token = '';
    let chatMsgs = null;
    let pollTimer = null;
    let refreshing = false;
    const seenIds = new Set();
    let titleFlashTimer = null;
    let origTitle = '';
    const REF_ICONS = { POLICY: 'gavel', GUIDE: 'play_circle' };
    const REF_LABELS = { POLICY: 'Quy định', GUIDE: 'Hướng dẫn' };
    function refUrlFor(type, id) {
        if (type === 'POLICY' && cfg?.urls?.policyDetail)
            return `${cfg.urls.policyDetail}?ids=${encodeURIComponent(id)}`;
        if (type === 'GUIDE' && cfg?.urls?.guideDetail)
            return `${cfg.urls.guideDetail}?id=${encodeURIComponent(id)}`;
        return '#';
    }

    // ─── Sanitize HTML (light) ───────────────────────────────
    function safeHtml(html) {
        if (html == null) return '';
        return String(html)
            .replace(/<script[\s\S]*?<\/script>/gi, '')
            .replace(/<iframe[\s\S]*?<\/iframe>/gi, '')
            .replace(/\son\w+\s*=\s*("[^"]*"|'[^']*'|[^\s>]+)/gi, '')
            .replace(/javascript:/gi, '');
    }

    function esc(s) {
        return (s == null ? '' : String(s))
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function classify(senderType) {
        if (senderType === 'SYS') return 'sys';
        return senderType === cfg.mySide ? 'mine' : 'theirs';
    }

    function avatarClass(senderType) {
        return senderType === cfg.mySide ? 'av-mine' : 'av-theirs';
    }

    function scrollBottom(force) {
        if (!chatMsgs) return;
        const atBottom = chatMsgs.scrollHeight - chatMsgs.scrollTop - chatMsgs.clientHeight < 100;
        if (force || atBottom) chatMsgs.scrollTop = chatMsgs.scrollHeight;
    }

    function init(config) {
        cfg = Object.assign({
            mySide: 'HR',
            isAnon: false,
            anonToken: '',
            isClosed: false,
            pollIntervalMs: 30000,
            reloadOnLock: false,
            recallableIfUnreadBy: 'EMP',
            defaultTheirsName: '',
            urls: {}
        }, config || {});

        token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
        chatMsgs = document.getElementById('chatMsgs');

        ensureRefViewerInjected();

        // populate seenIds from existing rendered messages
        if (chatMsgs) {
            chatMsgs.querySelectorAll('[data-msg-id]').forEach(el => {
                const id = parseInt(el.dataset.msgId || '0', 10);
                if (id > 0) { seenIds.add(id); if (id > lastMsgId) lastMsgId = id; }
            });
        }

        // sanitize any server-rendered .msg-content blocks
        document.querySelectorAll('.msg-content').forEach(el => {
            el.innerHTML = safeHtml(el.innerHTML);
        });

        // wire image lightbox via event delegation
        if (chatMsgs) {
            chatMsgs.addEventListener('click', onChatClick);
        }

        updateReadStatusOnLastMine();
        scrollBottom(true);

        if (!cfg.isClosed && cfg.pollIntervalMs > 0) {
            origTitle = document.title;
            pollTimer = setInterval(() => {
                if (document.hidden) return; // skip while tab hidden
                refreshMessages();
            }, cfg.pollIntervalMs);
            // Stop title flash whenever user comes back to tab
            document.addEventListener('visibilitychange', () => {
                if (!document.hidden) stopTitleFlash();
            });
        }

        // initial char counter sync (in case editor restored content)
        updateCharCounter(getInputLength());
    }

    // ─── Image lightbox ──────────────────────────────────────
    function onChatClick(e) {
        const img = e.target.closest('img.attach-img');
        if (!img) return;
        e.preventDefault();
        // try to find the full-size URL from parent <a>
        const anchor = img.closest('a');
        const fullUrl = anchor?.getAttribute('href') || img.src;
        openLightbox(fullUrl);
    }

    function openLightbox(url) {
        let lb = document.getElementById('imgLightbox');
        if (!lb) {
            lb = document.createElement('div');
            lb.id = 'imgLightbox';
            lb.className = 'img-lightbox';
            lb.innerHTML = '<button class="lb-close" aria-label="Close">✕</button><img alt="">';
            document.body.appendChild(lb);
            lb.addEventListener('click', () => closeLightbox());
            lb.querySelector('img').addEventListener('click', e => e.stopPropagation());
        }
        lb.querySelector('img').src = url;
        lb.style.display = 'flex';
        document.body.style.overflow = 'hidden';
        document.addEventListener('keydown', onLightboxKey);
    }

    function closeLightbox() {
        const lb = document.getElementById('imgLightbox');
        if (lb) lb.style.display = 'none';
        document.body.style.overflow = '';
        document.removeEventListener('keydown', onLightboxKey);
    }

    function onLightboxKey(e) {
        if (e.key === 'Escape') closeLightbox();
    }

    // ─── Append message ──────────────────────────────────────
    function appendMsg(m) {
        if (!chatMsgs) return;
        if (seenIds.has(m.id)) return;
        seenIds.add(m.id);
        if (m.id > lastMsgId) lastMsgId = m.id;

        const cls = classify(m.senderType);
        const avCls = avatarClass(m.senderType);
        const initial = m.senderName ? m.senderName[0].toUpperCase() : (m.senderType === 'EMP' ? 'E' : 'H');
        const t = new Date(m.sentDt).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit', hour12: false });

        let attachHtml = '';
        if (m.attachments?.length > 0) {
            attachHtml = '<div class="msg-attachments">' + m.attachments.map(a => {
                const fullUrl  = `${cfg.urls.getFile}?path=${encodeURIComponent(a.filePath)}`;
                if (a.fileType === 'IMAGE') {
                    const thumbUrl = `${cfg.urls.getFile}?path=${encodeURIComponent(a.thumbPath || a.filePath)}`;
                    return `<a href="${fullUrl}"><img class="attach-img" src="${thumbUrl}" alt="${esc(a.fileName)}"></a>`;
                }
                if (a.fileType === 'VIDEO') {
                    return `<video class="attach-video" controls preload="metadata" playsinline controlsList="nodownload" src="${fullUrl}"></video>`;
                }
                return `<a class="attach-file" href="${fullUrl}" download="${esc(a.fileName)}"><span class="material-symbols-rounded" style="font-size:1.1rem">description</span>${esc(a.fileName)}</a>`;
            }).join('') + '</div>';
        }

        let refsHtml = '';
        if (m.refs?.length > 0) {
            refsHtml = '<div class="msg-refs">' + m.refs.map(r => {
                const icon  = REF_ICONS[r.refType] || 'link';
                const label = REF_LABELS[r.refType] || r.refType;
                const url   = refUrlFor(r.refType, r.refId);
                return `<a class="msg-ref-card" href="${url}">
                    <div class="mrc-icon"><span class="material-symbols-rounded" style="font-size:1.1rem">${icon}</span></div>
                    <div class="mrc-info">
                        <div class="mrc-label">${label}</div>
                        <div class="mrc-title">${esc(r.refTitle || `#${r.refId}`)}</div>
                    </div>
                    <span class="material-symbols-rounded mrc-arrow">chevron_right</span>
                </a>`;
            }).join('') + '</div>';
        }

        const contentHtml = m.isDeleted
            ? '<span>Tin nhắn đã thu hồi</span>'
            : (m.content ? `<div class="msg-content">${safeHtml(m.content)}</div>` : '') + attachHtml + refsHtml;

        const showName = cls !== 'sys';
        const displayName = m.senderName
            || (m.senderType === cfg.mySide ? '' : (cfg.defaultTheirsName || (m.senderType === 'EMP' ? 'Nhân viên' : 'HR')));
        const nameRow = (showName && displayName)
            ? `<div class="msg-sender-name vni-font">${esc(displayName)}</div>` : '';

        const isMine = m.senderType === cfg.mySide
            && (cfg.mySide === 'EMP' || m.senderCd === cfg.myEmpCd);
        const unreadFlag = cfg.recallableIfUnreadBy === 'EMP' ? !m.isReadEmp : !m.isReadHr;
        const canRecall = isMine && !m.isDeleted && unreadFlag;
        const recallBtn = canRecall
            ? `<button class="msg-recall-btn" onclick="InquiryChat.recallMsg(${m.id})" title="Thu hồi"><span class="material-symbols-rounded">undo</span></button>`
            : '';

        const avatar = cls !== 'sys' ? `<div class="msg-avatar ${avCls}">${initial}</div>` : '';

        const bubbleHtml = `<div class="msg-bubble ${m.isDeleted ? 'deleted' : ''}">${contentHtml}<div class="msg-time">${t}</div></div>`;
        const bubbleRow = cls === 'sys'
            ? bubbleHtml
            : `<div class="msg-bubble-row">${bubbleHtml}${recallBtn}</div>`;

        const div = document.createElement('div');
        div.className = `msg-row ${cls}`;
        div.dataset.msgId = m.id;
        div.dataset.senderCd = m.senderCd || '';
        div.dataset.senderType = m.senderType;
        div.dataset.isMine = isMine ? '1' : '0';
        div.dataset.isReadHr  = m.isReadHr  ? '1' : '0';
        div.dataset.isReadEmp = m.isReadEmp ? '1' : '0';
        div.innerHTML = `${avatar}<div>${nameRow}${bubbleRow}</div>`;
        chatMsgs.appendChild(div);
    }

    // ─── Read status indicator on last "mine" message ────────
    function updateReadStatusOnLastMine() {
        if (!chatMsgs) return;
        chatMsgs.querySelectorAll('.msg-read-status').forEach(el => el.remove());
        const mineRows = chatMsgs.querySelectorAll('.msg-row.mine');
        if (mineRows.length === 0) return;
        const last = mineRows[mineRows.length - 1];
        const isReadByOther = cfg.recallableIfUnreadBy === 'EMP'
            ? last.dataset.isReadEmp === '1'
            : last.dataset.isReadHr === '1';
        const innerCol = last.querySelector('div:last-child');
        if (!innerCol) return;
        const status = document.createElement('div');
        status.className = 'msg-read-status' + (isReadByOther ? ' is-read' : '');
        status.innerHTML = isReadByOther
            ? '<span class="material-symbols-rounded" style="font-size:.85rem;vertical-align:middle">done_all</span> Đã đọc'
            : '<span class="material-symbols-rounded" style="font-size:.85rem;vertical-align:middle">done</span> Đã gửi';
        innerCol.appendChild(status);
    }

    async function refreshMessages() {
        if (refreshing) return;
        refreshing = true;
        try {
            const res = await fetch(`${cfg.urls.getMessages}?id=${cfg.inquiryId}&afterMsgId=${lastMsgId}`);
            const data = await res.json();
            if (data.success && data.messages?.length > 0) {
                const newFromOther = data.messages.filter(m =>
                    m.senderType && m.senderType !== 'SYS' && m.senderType !== cfg.mySide
                );
                data.messages.forEach(appendMsg);
                scrollBottom(true);
                if (newFromOther.length > 0) notifyIncoming(newFromOther.length);
            }
            // also refresh read flags on currently-rendered mine messages
            if (data.success && data.allReadStatus) {
                applyReadStatusBatch(data.allReadStatus);
            }
            updateReadStatusOnLastMine();

            // Detect status change OPEN → CLOSED (other party closed the chat)
            const newStatus = data?.inquiry?.status;
            if (newStatus === 'CLOSED' && !cfg.isClosed) {
                cfg.isClosed = true;
                if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
                stopTitleFlash();
                // For employee: reload so the rating modal can pop up automatically
                if (cfg.mySide === 'EMP') {
                    setTimeout(() => location.reload(), 600);
                } else {
                    // For HR/Admin: just show a toast; no need to reload
                    if (window.showToast) showToast('Hội thoại đã được đóng', 'info');
                }
            }
        } catch { /* ignore */ }
        finally { refreshing = false; }
    }

    function notifyIncoming(count) {
        try {
            const ctx = new (window.AudioContext || window.webkitAudioContext)();
            const o = ctx.createOscillator(), g = ctx.createGain();
            o.connect(g); g.connect(ctx.destination);
            o.frequency.value = 880; g.gain.value = 0.05;
            o.start(); setTimeout(() => { o.stop(); ctx.close(); }, 180);
        } catch {}
        if (document.hidden) flashTitle(`(${count}) Tin mới — ${origTitle}`);
    }

    function flashTitle(msg) {
        if (titleFlashTimer) return; // already flashing
        let on = false;
        titleFlashTimer = setInterval(() => {
            document.title = on ? origTitle : msg;
            on = !on;
        }, 1000);
    }
    function stopTitleFlash() {
        if (titleFlashTimer) { clearInterval(titleFlashTimer); titleFlashTimer = null; }
        if (origTitle) document.title = origTitle;
    }

    function applyReadStatusBatch(arr) {
        if (!Array.isArray(arr)) return;
        arr.forEach(s => {
            const row = chatMsgs?.querySelector(`[data-msg-id="${s.id}"]`);
            if (!row) return;
            row.dataset.isReadHr  = s.isReadHr  ? '1' : '0';
            row.dataset.isReadEmp = s.isReadEmp ? '1' : '0';
        });
    }

    // ─── Input helpers (editor or textarea fallback) ─────────
    function getInputHTML() {
        if (window.InquiryEditor) return window.InquiryEditor.getHTML();
        const input = document.getElementById('msgInput');
        return (input?.value ?? '').trim();
    }
    function getInputLength() {
        if (window.InquiryEditor) return window.InquiryEditor.getLength();
        return (document.getElementById('msgInput')?.value ?? '').length;
    }
    function clearInput() {
        if (window.InquiryEditor) window.InquiryEditor.clear();
        else { const input = document.getElementById('msgInput'); if (input) input.value = ''; }
        updateCharCounter(0);
    }

    function updateCharCounter(len) {
        const el = document.getElementById('charCounter');
        if (!el) return;
        el.textContent = `${len}/${MAX_CHARS}`;
        el.classList.toggle('warn',   len >= MAX_CHARS - 200 && len < MAX_CHARS);
        el.classList.toggle('danger', len >= MAX_CHARS);
        const btnSend = document.getElementById('btnSend');
        if (btnSend) btnSend.disabled = len > MAX_CHARS;
    }

    function onEditorUpdate(textLength) {
        updateCharCounter(textLength);
    }

    function autoResize(el) {
        el.style.height = 'auto';
        el.style.height = Math.min(el.scrollHeight, 120) + 'px';
        updateCharCounter(el.value.length);
    }

    function handleKeyDown(e) {
        if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(); }
    }

    // ─── File upload ─────────────────────────────────────────
    async function handleFiles(fileList) {
        if (uploadedFiles.length + fileList.length > 5) { showToast('Tối đa 5 file', 'error'); return; }
        for (const file of fileList) {
            const isVid    = /\.(mp4|webm|mov|m4v)$/i.test(file.name);
            const maxBytes = isVid ? 50 * 1024 * 1024 : 10 * 1024 * 1024;
            if (file.size > maxBytes) { showToast(`"${file.name}" vượt ${isVid ? '50MB' : '10MB'}`, 'error'); continue; }
            const fd = new FormData(); fd.append('file', file);
            try {
                const res = await fetch(cfg.urls.uploadFile, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token },
                    body: fd
                });
                const d = await res.json();
                if (!d.success) { showToast(d.message || 'Upload thất bại', 'error'); continue; }
                uploadedFiles.push(d);
                renderFilePreviews();
            } catch { showToast('Lỗi upload', 'error'); }
        }
        const input = document.getElementById('fileInput');
        if (input) input.value = '';
    }

    function renderFilePreviews() {
        const strip = document.getElementById('filePreviewStrip');
        if (!strip) return;
        strip.innerHTML = uploadedFiles.map((f, i) => {
            let thumb;
            if (f.thumbPath) {
                thumb = `<img src="${cfg.urls.getFile}?path=${encodeURIComponent(f.thumbPath)}" alt="">`;
            } else if (f.fileType === 'VIDEO') {
                thumb = `<span class="material-symbols-rounded" style="font-size:1.2rem;color:#7c3aed">movie</span>`;
            } else {
                thumb = `<span class="material-symbols-rounded" style="font-size:1.2rem;color:#64748b">description</span>`;
            }
            const shortName = f.fileName.length > 15 ? f.fileName.substr(0, 12) + '...' : f.fileName;
            return `<div class="file-chip">${thumb}<span>${esc(shortName)}</span><span class="fc-remove" onclick="InquiryChat.removeFile(${i})">✕</span></div>`;
        }).join('');
    }

    function removeFile(i) {
        uploadedFiles.splice(i, 1);
        renderFilePreviews();
    }

    // ─── Send ────────────────────────────────────────────────
    let sendingPromise = null;
    function sendMessage() {
        if (sendingPromise) return sendingPromise;
        sendingPromise = (async () => {
            const content = getInputHTML();
            const textLen = getInputLength();
            if (!content && uploadedFiles.length === 0 && selectedRefs.length === 0) return;
            if (textLen > MAX_CHARS) { showToast(`Tin nhắn vượt quá ${MAX_CHARS} ký tự`, 'error'); return; }

            const btn = document.getElementById('btnSend');
            if (btn) btn.disabled = true;

            const body = {
                inquiryId: cfg.inquiryId,
                inquiryNo: cfg.inquiryNo,
                content: content || null,
                files: uploadedFiles.length > 0 ? uploadedFiles : null,
                refs:  selectedRefs.length > 0
                    ? selectedRefs.map(r => ({ refType: r.refType, refId: r.refId }))
                    : null
            };
            if (cfg.isAnon) body.anonToken = cfg.anonToken;

            try {
                const res = await fetch(cfg.urls.send, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                    body: JSON.stringify(body)
                });
                const data = await res.json();
                if (!data.success) {
                    showToast(data.message || 'Gửi thất bại', 'error');
                } else {
                    clearInput();
                    uploadedFiles = [];
                    renderFilePreviews();
                    selectedRefs = [];
                    renderRefChips();
                    await refreshMessages();
                    if (cfg.reloadOnLock && data.justLocked) location.reload();
                }
            } catch { showToast('Lỗi kết nối', 'error'); }
            finally {
                if (btn) btn.disabled = false;
            }
        })();
        sendingPromise.finally(() => { sendingPromise = null; });
        return sendingPromise;
    }

    function recallMsg(msgId) {
        showConfirm('Thu hồi tin nhắn này?', async () => {
            const body = cfg.mySide === 'EMP'
                ? { msgId, anonToken: cfg.isAnon ? cfg.anonToken : null }
                : { id: msgId };
            const res = await fetch(cfg.urls.recall, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify(body)
            });
            const data = await res.json();
            if (!data.success) { showToast(data.message || 'Không thể thu hồi', 'error'); return; }
            const row = document.querySelector(`[data-msg-id="${msgId}"]`);
            if (row) {
                const bubble = row.querySelector('.msg-bubble');
                if (bubble) {
                    const timeHtml = bubble.querySelector('.msg-time')?.innerHTML ?? '';
                    bubble.innerHTML = '<span>Tin nhắn đã thu hồi</span><div class="msg-time">' + timeHtml + '</div>';
                    bubble.classList.add('deleted');
                }
                const btn = row.querySelector('.msg-recall-btn');
                if (btn) btn.remove();
            }
        }, { title: 'Thu hồi tin nhắn', okText: 'Thu hồi', okClass: 'btn-danger', icon: 'undo', iconColor: '#ef4444', iconBg: '#fef2f2' });
    }

    function confirmClose() {
        const msg = cfg.mySide === 'EMP'
            ? 'Đóng hội thoại này? Bạn sẽ không thể gửi thêm tin nhắn.'
            : 'Đóng hội thoại này?';
        showConfirm(msg, async () => {
            const body = { inquiryId: cfg.inquiryId, closeNote: null };
            if (cfg.isAnon) body.anonToken = cfg.anonToken;
            const res = await fetch(cfg.urls.close, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify(body)
            });
            const data = await res.json();
            if (!data.success) { showToast(data.message || 'Không thể đóng', 'error'); return; }
            showToast(cfg.mySide === 'EMP' ? 'Hội thoại đã đóng' : 'Đã đóng hội thoại', 'success');
            setTimeout(() => location.reload(), 800);
        }, { title: 'Đóng hội thoại', okText: 'Đóng', okClass: 'btn-warning', icon: 'close', iconColor: '#d97706', iconBg: '#fef3c7' });
    }

    function confirmUnlock() {
        if (!cfg.urls.unlock) return;
        showConfirm('Mở khóa hội thoại này? HR hiện tại sẽ mất quyền phụ trách.', async () => {
            const res = await fetch(cfg.urls.unlock, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({ id: cfg.inquiryId })
            });
            const data = await res.json();
            if (!data.success) { showToast(data.message || 'Không thể mở khóa', 'error'); return; }
            showToast('Đã mở khóa hội thoại', 'success');
            setTimeout(() => location.reload(), 800);
        }, { title: 'Mở khóa hội thoại', okText: 'Mở khóa', okClass: 'btn-secondary', icon: 'lock_open', iconColor: '#475569', iconBg: '#f1f5f9' });
    }

    // ─── Ref picker (HR/Admin only) ──────────────────────────
    function renderRefChips() {
        const strip = document.getElementById('refChipsStrip');
        if (!strip) return;
        strip.innerHTML = selectedRefs.map((r, i) => {
            const icon = REF_ICONS[r.refType] || 'link';
            return `<div class="ref-chip">
                <span class="material-symbols-rounded rc-icon">${icon}</span>
                <span class="rc-title" title="${esc(r.refTitle || '')}">${esc(r.refTitle || ('#' + r.refId))}</span>
                <span class="rc-remove" onclick="InquiryChat.removeRef(${i})">✕</span>
            </div>`;
        }).join('');
    }

    function removeRef(i) {
        selectedRefs.splice(i, 1);
        renderRefChips();
    }

    let pickerState = { type: 'POLICY', q: '', searchTimer: null };

    function openRefPicker() {
        if (!cfg.urls.searchRefs) return;
        let modal = document.getElementById('refPickerModal');
        if (!modal) {
            modal = document.createElement('div');
            modal.id = 'refPickerModal';
            modal.className = 'refp-overlay';
            modal.innerHTML = `
                <div class="refp-modal" onclick="event.stopPropagation()">
                    <div class="refp-header">
                        <div class="refp-title">Chọn nội dung trích dẫn</div>
                        <button class="refp-close" onclick="InquiryChat.closeRefPicker()">✕</button>
                    </div>
                    <div class="refp-tabs">
                        <button class="refp-tab is-active" data-type="POLICY" onclick="InquiryChat.switchRefTab('POLICY')">Quy định</button>
                        <button class="refp-tab"           data-type="GUIDE"  onclick="InquiryChat.switchRefTab('GUIDE')">Hướng dẫn</button>
                    </div>
                    <div class="refp-search">
                        <input type="text" id="refPickerSearch" placeholder="Tìm theo tiêu đề / danh mục..." oninput="InquiryChat.onRefSearch(this.value)">
                    </div>
                    <div class="refp-list" id="refPickerList">
                        <div class="refp-empty">Đang tải...</div>
                    </div>
                </div>`;
            document.body.appendChild(modal);
            modal.addEventListener('click', e => {
                if (e.target === modal) closeRefPicker();
            });
        }
        modal.style.display = 'flex';
        document.body.style.overflow = 'hidden';
        pickerState.type = 'POLICY';
        pickerState.q = '';
        const input = document.getElementById('refPickerSearch');
        if (input) input.value = '';
        document.querySelectorAll('#refPickerModal .refp-tab').forEach(t => {
            t.classList.toggle('is-active', t.dataset.type === 'POLICY');
        });
        loadRefList();
    }

    function closeRefPicker() {
        const modal = document.getElementById('refPickerModal');
        if (modal) modal.style.display = 'none';
        document.body.style.overflow = '';
    }

    function switchRefTab(type) {
        pickerState.type = type;
        document.querySelectorAll('#refPickerModal .refp-tab').forEach(t => {
            t.classList.toggle('is-active', t.dataset.type === type);
        });
        loadRefList();
    }

    function onRefSearch(val) {
        pickerState.q = val;
        clearTimeout(pickerState.searchTimer);
        pickerState.searchTimer = setTimeout(loadRefList, 300);
    }

    async function loadRefList() {
        const list = document.getElementById('refPickerList');
        if (!list) return;
        list.innerHTML = '<div class="refp-empty">Đang tải...</div>';
        try {
            const url = `${cfg.urls.searchRefs}?type=${pickerState.type}&q=${encodeURIComponent(pickerState.q || '')}`;
            const res = await fetch(url);
            const data = await res.json();
            if (!data.success) {
                list.innerHTML = `<div class="refp-empty">Lỗi: ${esc(data.message || '')}</div>`;
                return;
            }
            const items = data.data || [];
            if (items.length === 0) {
                list.innerHTML = '<div class="refp-empty">Không tìm thấy kết quả</div>';
                return;
            }
            const icon = REF_ICONS[pickerState.type] || 'link';
            list.innerHTML = items.map(it => `
                <div class="refp-item" data-ref-type="${esc(it.refType)}" data-ref-id="${it.id}" data-ref-title="${esc(it.title || '')}" data-ref-cat="${esc(it.category || '')}">
                    <div class="ri-icon"><span class="material-symbols-rounded" style="font-size:1.1rem">${icon}</span></div>
                    <div class="ri-info">
                        <div class="ri-cat">${esc(it.category || '')}</div>
                        <div class="ri-title">${esc(it.title || '')}</div>
                    </div>
                    <button class="ri-pick" data-action="pick">Chọn</button>
                </div>`).join('');
            // bind clicks via delegation (idempotent: re-binding fine since old handler is reattached on every render)
            list.onclick = (e) => {
                const item = e.target.closest('.refp-item');
                if (!item) return;
                pickRef(
                    item.dataset.refType,
                    parseInt(item.dataset.refId, 10),
                    item.dataset.refTitle,
                    item.dataset.refCat
                );
            };
        } catch (e) {
            list.innerHTML = `<div class="refp-empty">Lỗi kết nối</div>`;
        }
    }

    function pickRef(refType, refId, refTitle, category) {
        // tránh trùng
        if (selectedRefs.some(r => r.refType === refType && r.refId === refId)) {
            showToast('Đã chọn nội dung này rồi', 'warning');
            return;
        }
        if (selectedRefs.length >= 5) {
            showToast('Tối đa 5 trích dẫn mỗi tin nhắn', 'warning');
            return;
        }
        selectedRefs.push({ refType, refId, refTitle, category });
        renderRefChips();
        closeRefPicker();
    }

    // ─── Ref viewer modal (mobile-friendly) ───────────────────────────────
    let _refViewerInjected = false;
    function ensureRefViewerInjected() {
        if (_refViewerInjected) return;
        _refViewerInjected = true;

        const style = document.createElement('style');
        style.textContent = `
            .rv-modal {
                position: fixed; inset: 0; background: rgba(15,23,42,.55);
                z-index: 2147483600; display: none;
                align-items: center; justify-content: center;
            }
            .rv-modal.show { display: flex; }
            .rv-modal-box {
                background: #fff; width: 100%; height: 100%;
                display: flex; flex-direction: column; overflow: hidden;
            }
            @media (min-width: 768px) {
                .rv-modal-box {
                    width: min(960px, 92vw); height: min(720px, 88vh);
                    border-radius: 14px; box-shadow: 0 18px 50px rgba(0,0,0,.28);
                }
            }
            .rv-head {
                display: flex; align-items: center; gap: .6rem;
                padding: .65rem .85rem;
                background: linear-gradient(135deg, #7f1d1d, #dc2626);
                color: #fff;
            }
            .rv-head .rv-icon { font-size: 1.2rem; }
            .rv-head .rv-title { flex: 1; font-weight: 800; font-size: .95rem;
                white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: #fff; }
            .rv-head .rv-btn {
                background: rgba(255,255,255,.18); border: 1px solid rgba(255,255,255,.32);
                color: #fff; border-radius: 8px; padding: .3rem .55rem;
                font-size: .75rem; font-weight: 700; cursor: pointer;
                display: inline-flex; align-items: center; gap: .2rem;
            }
            .rv-head .rv-btn:hover { background: rgba(255,255,255,.28); }
            .rv-head .rv-close { padding: .3rem .5rem; }
            .rv-body { flex: 1; position: relative; background: #f8fafc; }
            .rv-body iframe { position: absolute; inset: 0; width: 100%; height: 100%;
                border: 0; background: #fff; }
            .rv-loading {
                position: absolute; inset: 0; display: flex; align-items: center;
                justify-content: center; color: #94a3b8; font-size: .85rem; gap: .35rem;
            }
            .rv-modal.show .rv-loading.hidden { display: none; }
        `;
        document.head.appendChild(style);

        const modal = document.createElement('div');
        modal.className = 'rv-modal';
        modal.id = 'rvModal';
        modal.innerHTML = `
            <div class="rv-modal-box">
                <div class="rv-head">
                    <span class="material-symbols-rounded rv-icon" id="rvHeadIcon">link</span>
                    <span class="rv-title" id="rvHeadTitle">Trích dẫn</span>
                    <button class="rv-btn rv-open-new" id="rvOpenNew" title="Mở trong tab mới">
                        <span class="material-symbols-rounded" style="font-size:1rem">open_in_new</span>
                    </button>
                    <button class="rv-btn rv-close" id="rvClose" title="Đóng">
                        <span class="material-symbols-rounded" style="font-size:1.1rem">close</span>
                    </button>
                </div>
                <div class="rv-body">
                    <div class="rv-loading" id="rvLoading">
                        <span class="material-symbols-rounded">hourglass_top</span> Đang tải...
                    </div>
                    <iframe id="rvFrame" src="about:blank"></iframe>
                </div>
            </div>
        `;
        document.body.appendChild(modal);

        modal.addEventListener('click', (e) => {
            if (e.target === modal) closeRefViewer();
        });
        document.getElementById('rvClose').addEventListener('click', closeRefViewer);
        document.getElementById('rvOpenNew').addEventListener('click', () => {
            const u = document.getElementById('rvFrame').src;
            // Điều hướng cùng tab thay vì window.open — tránh bug rớt session trên WebView mobile
            // (đây là link nội bộ cần cookie đăng nhập, không phải trang ngoài).
            if (u && u !== 'about:blank') window.location.href = u;
        });
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && modal.classList.contains('show')) closeRefViewer();
        });

        // delegate clicks on ref cards
        document.addEventListener('click', (e) => {
            const a = e.target.closest('.msg-ref-card');
            if (!a) return;
            const href = a.getAttribute('href');
            if (!href || href === '#') return;
            e.preventDefault();
            const titleEl = a.querySelector('.mrc-title');
            const iconEl  = a.querySelector('.mrc-icon .material-symbols-rounded');
            openRefViewer(
                href,
                titleEl ? titleEl.textContent.trim() : 'Trích dẫn',
                iconEl  ? iconEl.textContent.trim()  : 'link'
            );
        });
    }

    function openRefViewer(url, title, icon) {
        const modal   = document.getElementById('rvModal');
        const frame   = document.getElementById('rvFrame');
        const loading = document.getElementById('rvLoading');
        document.getElementById('rvHeadTitle').textContent = title || 'Trích dẫn';
        document.getElementById('rvHeadIcon').textContent  = icon  || 'link';
        loading.classList.remove('hidden');
        frame.onload = () => loading.classList.add('hidden');
        // Append embed=1 so the loaded page renders without header/sidebar/bottom-nav
        const sep   = url.includes('?') ? '&' : '?';
        const embed = url.includes('embed=1') ? url : url + sep + 'embed=1';
        frame.src   = embed;
        modal.classList.add('show');
        document.body.style.overflow = 'hidden';
    }

    function closeRefViewer() {
        const modal = document.getElementById('rvModal');
        const frame = document.getElementById('rvFrame');
        modal.classList.remove('show');
        frame.src = 'about:blank';
        document.body.style.overflow = '';
    }

    return {
        init, appendMsg, refreshMessages,
        sendMessage, recallMsg, confirmClose, confirmUnlock,
        handleFiles, removeFile, autoResize, handleKeyDown,
        onEditorUpdate, safeHtml, MAX_CHARS,
        openRefPicker, closeRefPicker, switchRefTab, onRefSearch, pickRef, removeRef
    };
})();
