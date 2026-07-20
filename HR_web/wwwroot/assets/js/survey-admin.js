// ══════════════════════════════════════════════════════════════
// SURVEY-ADMIN — Wizard tạo/sửa survey (4 bước).
// Dữ liệu ban đầu từ window.SURVEY_EDIT (Edit.cshtml inject).
// Save + Publish gọi /SurveyAdmin/Save (+ /ChangeStatus khi Publish).
// ══════════════════════════════════════════════════════════════
(function () {
    const E = window.SURVEY_EDIT || {};
    if (!E) return;

    const URLS = E.urls || {};
    const TOKEN = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]')?.value ?? '';

    // ── State ─────────────────────────────────────────
    let currentStep = 1;
    const totalSteps = 4;

    // Questions & scope state (mutable arrays).
    const state = {
        SURVEY_TYPE:    E.initial?.SURVEY_TYPE   || 'POLL',
        LANG:           E.initial?.LANG          || 'VI',
        RECIPIENT_MODE: E.initial?.RECIPIENT_MODE|| 'ALL',
        QUESTIONS:      (E.initial?.QUESTIONS || []).map(cloneQuestion),
        SCOPES:         (E.initial?.SCOPES    || []).map(s => ({ ...s })),
    };

    function cloneQuestion(q) {
        return {
            ID:            q.ID,
            QUESTION_TEXT: q.QUESTION_TEXT || '',
            QUESTION_TYPE: q.QUESTION_TYPE || 'SINGLE',
            IS_REQUIRED:   q.IS_REQUIRED ?? 1,
            DISPLAY_ORDER: q.DISPLAY_ORDER || 0,
            POINTS:        q.POINTS || 0,
            OPTIONS: (q.OPTIONS || []).map(o => ({
                ID: o.ID,
                OPTION_TEXT: o.OPTION_TEXT || '',
                DISPLAY_ORDER: o.DISPLAY_ORDER || 0,
                IS_CORRECT: o.IS_CORRECT || 0,
            })),
        };
    }

    // ── Step navigation ─────────────────────────
    const $btnPrev  = document.getElementById('btnPrev');
    const $btnNext  = document.getElementById('btnNext');
    const $btnDraft = document.getElementById('btnSaveDraft');
    const $btnPub   = document.getElementById('btnPublish');

    // EN survey → chỉ gửi toàn Expat, bỏ bước Phạm vi (step 3).
    const isStepSkipped = (n) => n === 3 && state.LANG === 'EN';
    function nextValidStep(n, dir) {
        let s = n + dir;
        while (s >= 1 && s <= totalSteps && isStepSkipped(s)) s += dir;
        return s;
    }

    function showStep(n) {
        // Nếu vô step bị skip → nhảy sang step tiếp theo/trước theo hướng
        if (isStepSkipped(n)) n = n < currentStep ? nextValidStep(n, -1) : nextValidStep(n, 1);

        currentStep = n;
        document.querySelectorAll('.sa-panel').forEach(p => {
            p.classList.toggle('d-none', p.dataset.panel !== String(n));
        });
        document.querySelectorAll('.sa-step').forEach(s => {
            const step = parseInt(s.dataset.step, 10);
            s.classList.toggle('active', step === n);
            s.classList.toggle('done',   step < n && !isStepSkipped(step));
            s.classList.toggle('d-none', isStepSkipped(step));
        });
        document.querySelectorAll('.sa-step-line[data-line-for]').forEach(ln => {
            const target = parseInt(ln.dataset.lineFor, 10);
            ln.classList.toggle('d-none', isStepSkipped(target));
        });
        $btnPrev.disabled = n === 1;
        const isLast = n === totalSteps;
        $btnNext.classList.toggle('d-none', isLast);
        $btnPub.classList.toggle('d-none', !isLast);

        // Re-render step-specific
        if (n === 2) renderQuestions();
        if (n === 3) renderScope();
        if (n === 4) updatePassScoreVisibility();
    }
    $btnPrev.addEventListener('click', () => {
        if (currentStep <= 1) return;
        showStep(nextValidStep(currentStep, -1));
    });
    $btnNext.addEventListener('click', () => {
        const err = validateStep(currentStep);
        if (err) { toast(err, 'warning'); return; }
        showStep(nextValidStep(currentStep, 1));
    });

    function toast(msg, type) {
        if (window.showToast) return window.showToast(msg, type || 'error');
        alert(msg);
    }
    function confirmDialog(message, opts, onOk) {
        if (window.showConfirm) {
            window.showConfirm(message, onOk, opts || {});
        } else if (confirm(message)) {
            onOk();
        }
    }

    document.querySelectorAll('.sa-step').forEach(s => {
        s.addEventListener('click', () => {
            const step = parseInt(s.dataset.step, 10);
            if (step < currentStep) showStep(step);  // chỉ cho back
        });
    });

    // ── Step 1 — Type/Lang radio ─────────────────
    document.querySelectorAll('input[name="SURVEY_TYPE"]').forEach(r => {
        r.addEventListener('change', () => {
            state.SURVEY_TYPE = r.value;
            document.querySelectorAll('input[name="SURVEY_TYPE"]').forEach(rr => {
                rr.closest('.sa-radio-card').classList.toggle('active', rr.checked);
            });
        });
    });
    document.querySelectorAll('input[name="LANG"]').forEach(r => {
        r.addEventListener('change', () => {
            state.LANG = r.value;
            document.querySelectorAll('input[name="LANG"]').forEach(rr => {
                rr.closest('.sa-radio-card').classList.toggle('active', rr.checked);
            });
            // EN → force mode ALL và ẩn bước Phạm vi
            if (state.LANG === 'EN') {
                state.RECIPIENT_MODE = 'ALL';
                document.querySelectorAll('input[name="RECIPIENT_MODE"]').forEach(rr => {
                    rr.checked = rr.value === 'ALL';
                    rr.closest('.sa-radio-card').classList.toggle('active', rr.checked);
                });
            }
            // Cập nhật lại step indicator + nhảy step nếu đang ở step bị skip
            showStep(currentStep);
        });
    });

    // ── Step 2 — Questions builder ─────────────
    document.getElementById('btnAddQuestion').addEventListener('click', () => {
        state.QUESTIONS.push({
            QUESTION_TEXT: '',
            QUESTION_TYPE: 'SINGLE',
            IS_REQUIRED: 1,
            DISPLAY_ORDER: state.QUESTIONS.length + 1,
            POINTS: 0,
            OPTIONS: [
                { OPTION_TEXT: '', DISPLAY_ORDER: 1, IS_CORRECT: 0 },
                { OPTION_TEXT: '', DISPLAY_ORDER: 2, IS_CORRECT: 0 },
            ],
        });
        renderQuestions();
    });

    function renderQuestions() {
        const $list = document.getElementById('questionList');
        const isQuiz = state.SURVEY_TYPE === 'QUIZ';

        // Show quiz info nếu QUIZ
        document.querySelector('.sa-quiz-info').classList.toggle('d-none', !isQuiz);

        if (state.QUESTIONS.length === 0) {
            $list.innerHTML = '<div class="text-muted text-center py-3">Chưa có câu hỏi. Thêm bằng nút bên dưới.</div>';
            document.getElementById('totalPoints').textContent = '0';
            return;
        }

        $list.innerHTML = state.QUESTIONS.map((q, idx) => renderQuestionCard(q, idx, isQuiz)).join('');
        wireQuestionCards();
        updateTotalPoints();
    }

    function renderQuestionCard(q, idx, isQuiz) {
        // QUIZ không cho TEXT (tự luận) và RATING vì không chấm điểm được.
        const types = isQuiz ? [
            ['SINGLE',   'Single choice'],
            ['MULTI',    'Multiple choice'],
            ['YESNO',    'Yes / No'],
            ['DROPDOWN', 'Dropdown'],
        ] : [
            ['SINGLE',   'Single choice'],
            ['MULTI',    'Multiple choice'],
            ['YESNO',    'Yes / No'],
            ['RATING',   'Rating 1-5'],
            ['TEXT',     'Text free'],
            ['DROPDOWN', 'Dropdown'],
        ];
        // Nếu type hiện tại không hợp lệ khi QUIZ, ép về SINGLE + tạo 2 options mặc định
        if (isQuiz && !['SINGLE','MULTI','YESNO','DROPDOWN'].includes(q.QUESTION_TYPE)) {
            q.QUESTION_TYPE = 'SINGLE';
            if (!q.OPTIONS || q.OPTIONS.length === 0) {
                q.OPTIONS = [
                    { OPTION_TEXT: '', DISPLAY_ORDER: 1, IS_CORRECT: 0 },
                    { OPTION_TEXT: '', DISPLAY_ORDER: 2, IS_CORRECT: 0 },
                ];
            }
        }
        const typeOpts = types.map(([v, l]) =>
            `<option value="${v}" ${q.QUESTION_TYPE === v ? 'selected' : ''}>${l}</option>`
        ).join('');

        const hasOptions = ['SINGLE','MULTI','YESNO','DROPDOWN'].includes(q.QUESTION_TYPE);

        return `
        <div class="sa-question-card" data-qidx="${idx}">
            <div class="sa-question-header">
                <div class="sa-question-num">${idx + 1}</div>
                <input type="text" class="form-control form-control-sm q-text"
                       placeholder="Nội dung câu hỏi" maxlength="1000"
                       value="${escapeAttr(q.QUESTION_TEXT)}" />
                <select class="form-select form-select-sm q-type" style="max-width: 160px;">
                    ${typeOpts}
                </select>
                ${isQuiz ? `
                <div class="sa-q-points-wrap d-flex align-items-center gap-1" title="Số điểm câu này khi user trả lời đúng">
                    <label class="form-label small text-muted mb-0 fw-semibold">Điểm:</label>
                    <input type="number" class="form-control form-control-sm q-points" style="max-width:80px" min="0" step="1"
                           placeholder="0" value="${q.POINTS || 0}" />
                </div>
                ` : ''}
                <div class="form-check form-switch mb-0 ms-1 d-flex align-items-center gap-1" title="Bắt buộc trả lời">
                    <input class="form-check-input q-required" type="checkbox" ${q.IS_REQUIRED === 1 ? 'checked' : ''} />
                    <label class="form-check-label small text-muted mb-0">Bắt buộc</label>
                </div>
                <button type="button" class="btn btn-sm btn-outline-danger q-del" title="Xoá câu hỏi">
                    <i class="bi bi-trash"></i>
                </button>
            </div>
            ${hasOptions ? renderOptionsBlock(q, isQuiz) : ''}
        </div>`;
    }

    function renderOptionsBlock(q, isQuiz) {
        const rows = (q.OPTIONS || []).map((o, oIdx) => `
            <div class="sa-option-row" data-oidx="${oIdx}">
                ${isQuiz ? `
                <div class="form-check">
                    <input class="form-check-input o-correct" type="${q.QUESTION_TYPE === 'MULTI' ? 'checkbox' : 'radio'}" name="qcorrect_${q.__uid || Math.random()}"
                           ${o.IS_CORRECT === 1 ? 'checked' : ''} title="Đáp án đúng" />
                </div>` : ''}
                <input type="text" class="form-control form-control-sm o-text"
                       placeholder="Nội dung đáp án" maxlength="500"
                       value="${escapeAttr(o.OPTION_TEXT)}" />
                <button type="button" class="btn btn-sm btn-outline-danger o-del" title="Xoá">
                    <i class="bi bi-x"></i>
                </button>
            </div>`).join('');

        return `
        <div class="sa-question-body">
            ${rows}
            <button type="button" class="btn btn-sm btn-outline-primary o-add">
                <i class="bi bi-plus"></i> Thêm đáp án
            </button>
        </div>`;
    }

    function wireQuestionCards() {
        document.querySelectorAll('.sa-question-card').forEach(card => {
            const idx = parseInt(card.dataset.qidx, 10);
            const q   = state.QUESTIONS[idx];

            card.querySelector('.q-text').addEventListener('input', e => q.QUESTION_TEXT = e.target.value);
            card.querySelector('.q-type').addEventListener('change', e => {
                q.QUESTION_TYPE = e.target.value;
                if (['SINGLE','MULTI','YESNO','DROPDOWN'].includes(q.QUESTION_TYPE) && q.OPTIONS.length === 0) {
                    q.OPTIONS = [{OPTION_TEXT:'',DISPLAY_ORDER:1,IS_CORRECT:0},{OPTION_TEXT:'',DISPLAY_ORDER:2,IS_CORRECT:0}];
                } else if (['RATING','TEXT'].includes(q.QUESTION_TYPE)) {
                    q.OPTIONS = [];
                }
                renderQuestions();
            });
            card.querySelector('.q-points')?.addEventListener('input', e => {
                q.POINTS = Number(e.target.value) || 0;
                updateTotalPoints();
            });
            card.querySelector('.q-required').addEventListener('change', e => q.IS_REQUIRED = e.target.checked ? 1 : 0);
            card.querySelector('.q-del').addEventListener('click', () => {
                confirmDialog('Xoá câu hỏi này?', {
                    title: 'Xoá câu hỏi', okText: 'Xoá', okClass: 'btn-danger',
                    icon: 'delete', iconColor: '#dc2626', iconBg: '#fee2e2'
                }, () => { state.QUESTIONS.splice(idx, 1); renderQuestions(); });
            });

            card.querySelectorAll('.o-text').forEach(el => {
                el.addEventListener('input', e => {
                    const oIdx = parseInt(el.closest('.sa-option-row').dataset.oidx, 10);
                    q.OPTIONS[oIdx].OPTION_TEXT = e.target.value;
                });
            });
            card.querySelectorAll('.o-correct').forEach(el => {
                el.addEventListener('change', e => {
                    const oIdx = parseInt(el.closest('.sa-option-row').dataset.oidx, 10);
                    if (q.QUESTION_TYPE === 'MULTI') {
                        q.OPTIONS[oIdx].IS_CORRECT = e.target.checked ? 1 : 0;
                    } else {
                        q.OPTIONS.forEach((o, i) => o.IS_CORRECT = i === oIdx ? 1 : 0);
                        renderQuestions();  // update UI để reflect radio state
                    }
                });
            });
            card.querySelectorAll('.o-del').forEach(el => {
                el.addEventListener('click', () => {
                    const oIdx = parseInt(el.closest('.sa-option-row').dataset.oidx, 10);
                    q.OPTIONS.splice(oIdx, 1);
                    renderQuestions();
                });
            });
            card.querySelector('.o-add')?.addEventListener('click', () => {
                q.OPTIONS.push({ OPTION_TEXT:'', DISPLAY_ORDER: q.OPTIONS.length + 1, IS_CORRECT: 0 });
                renderQuestions();
            });
        });
    }

    function updateTotalPoints() {
        const total = state.QUESTIONS.reduce((sum, q) => sum + (Number(q.POINTS) || 0), 0);
        const el = document.getElementById('totalPoints');
        if (el) el.textContent = total;
        const hint = document.getElementById('passScoreHintTotal');
        if (hint) hint.textContent = total;
    }

    // ── Step 3 — Scope ─────────────────────────
    document.querySelectorAll('input[name="RECIPIENT_MODE"]').forEach(r => {
        r.addEventListener('change', () => {
            state.RECIPIENT_MODE = r.value;
            document.querySelectorAll('input[name="RECIPIENT_MODE"]').forEach(rr => {
                rr.closest('.sa-radio-card').classList.toggle('active', rr.checked);
            });
            renderScope();
        });
    });

    function renderScope() {
        const $dlw   = document.getElementById('scopeDLW');
        const $empcd = document.getElementById('scopeEMPCD');
        const mode   = state.RECIPIENT_MODE;

        $dlw.classList.toggle('d-none',   !(mode === 'DEPT_LINE_WORK' || mode === 'MIXED'));
        $empcd.classList.toggle('d-none', !(mode === 'EMPCD_LIST'      || mode === 'MIXED'));

        // DLW rows — hydrate lần đầu từ state.SCOPES nếu có
        if (mode === 'DEPT_LINE_WORK' || mode === 'MIXED') {
            const $list = document.getElementById('scopeDLWList');
            if (!$list.dataset.filled) {
                const dlwRows = state.SCOPES.filter(s => s.SCOPE_TYPE === 'DEPT_LINE_WORK');
                if (dlwRows.length === 0) {
                    addDLWRow();
                } else {
                    dlwRows.forEach(r => addDLWRow(r));
                }
                $list.dataset.filled = '1';
            }
        }
        // EMPCD textarea (populate 1 lần từ initial nếu có)
        const $ta = document.getElementById('fEmpcdList');
        if (!$ta.dataset.filled) {
            const list = state.SCOPES.filter(s => s.SCOPE_TYPE === 'EMPCD').map(s => s.EMPCD);
            $ta.value = list.join('\n');
            $ta.dataset.filled = '1';
        }
    }

    document.getElementById('btnAddScopeDLW').addEventListener('click', addDLWRow);

    async function updateDLWCount(row) {
        const dept  = row.querySelector('.dlw-dept').value;
        const line  = row.querySelector('.dlw-line').value;
        const work  = row.querySelector('.dlw-work').value;
        const badge = row.querySelector('.dlw-count');
        if (!dept || !badge) { if (badge) badge.textContent = ''; updateTotalCount(); return; }
        badge.textContent = '…';
        try {
            const qs = new URLSearchParams({ deptcd: dept });
            if (line) qs.set('linecd', line);
            if (work) qs.set('workcd', work);
            const r = await fetch(URLS.scopeCount + '?' + qs);
            const j = await r.json();
            badge.textContent = (j.count || 0) + ' NV';
        } catch { badge.textContent = ''; }
        updateTotalCount();
    }

    function updateTotalCount() {
        const total = [...document.querySelectorAll('#scopeDLWList .dlw-count')]
            .reduce((s, b) => s + (parseInt(b.textContent) || 0), 0);
        const el = document.getElementById('scopeTotalCount');
        if (el) el.textContent = total > 0 ? 'Tổng ước tính: ' + total + ' nhân viên' : '';
    }

    let _dlwN = 0;
    async function addDLWRow(prefill) {
        const tmpl  = document.getElementById('tmplScopeDLW').content.cloneNode(true);
        const row   = tmpl.querySelector('.sa-dlw-row');
        const n     = ++_dlwN;
        const $dept = row.querySelector('.dlw-dept');
        const $line = row.querySelector('.dlw-line');
        const $work = row.querySelector('.dlw-work');

        $dept.id = 'dlw-dept-' + n;
        $line.id = 'dlw-line-' + n;
        $work.id = 'dlw-work-' + n;

        // Select2 AJAX config — giống _OTFilterBar
        $dept.dataset.url         = URLS.getDept;
        $dept.dataset.placeholder = 'Chọn Dept...';
        $line.dataset.url         = URLS.getLine;
        $line.dataset.parent      = '#dlw-dept-' + n;
        $line.dataset.placeholder = 'Tất cả Line';
        $line.dataset.prependAll  = 'Tất cả';
        $work.dataset.url         = URLS.getWork;
        $work.dataset.parent      = '#dlw-line-' + n;
        $work.dataset.parentKey   = 'lineCd';
        $work.dataset.parent2     = '#dlw-dept-' + n;
        $work.dataset.parent2Key  = 'deptCd';
        $work.dataset.placeholder = 'Tất cả Work';
        $work.dataset.prependAll  = 'Tất cả';

        // Select2Helper.init tìm theo class .select2
        [$dept, $line, $work].forEach(s => s.classList.add('select2'));

        document.getElementById('scopeDLWList').appendChild(row);
        Select2Helper.init($(row));

        // Work bị disabled cho đến khi user chọn Line cụ thể
        $work.disabled = true;

        const $s = (id) => $('#' + id);
        $s('dlw-dept-' + n).on('change', function () {
            if (row.dataset.prefilling) return;
            $s('dlw-line-' + n).val(null).trigger('change');
            // line change handler sẽ clear + disable work
            updateDLWCount(row);
        });
        $s('dlw-line-' + n).on('change', function () {
            if (row.dataset.prefilling) return;
            const lineVal = $(this).val();
            const $w = $s('dlw-work-' + n);
            if (lineVal) {
                $w.prop('disabled', false).val(null).trigger('change');
            } else {
                $w.val(null).trigger('change').prop('disabled', true);
            }
            updateDLWCount(row);
        });
        $s('dlw-work-' + n).on('change', () => updateDLWCount(row));

        row.querySelector('.btn-del-dlw').addEventListener('click', () => {
            [$dept, $line, $work].forEach(s => { try { $(s).select2('destroy'); } catch {} });
            row.remove();
            updateTotalCount();
        });

        if (prefill?.DEPTCD) {
            row.dataset.prefilling = '1';
            try {
                const depts = await fetchDropdown(URLS.getDept, { id: prefill.DEPTCD });
                if (depts.length) $s('dlw-dept-' + n).append(new Option(depts[0].text, depts[0].id, true, true)).trigger('change');
                if (prefill.LINECD) {
                    const lines = await fetchDropdown(URLS.getLine, { deptCd: prefill.DEPTCD });
                    const ln    = lines.find(l => l.id === prefill.LINECD);
                    if (ln) $s('dlw-line-' + n).append(new Option(ln.text, ln.id, true, true)).trigger('change');
                    // Line có giá trị → enable Work
                    $s('dlw-work-' + n).prop('disabled', false);
                    if (prefill.WORKCD) {
                        const works = await fetchDropdown(URLS.getWork, { lineCd: prefill.LINECD, deptCd: prefill.DEPTCD });
                        const wk    = works.find(w => w.id === prefill.WORKCD);
                        if (wk) $s('dlw-work-' + n).append(new Option(wk.text, wk.id, true, true)).trigger('change');
                    }
                }
                // nếu không có LINECD → Work vẫn disabled (default)
            } finally {
                delete row.dataset.prefilling;
            }
        }
        await updateDLWCount(row);
    }

    async function fetchDropdown(url, params) {
        try {
            const qs  = new URLSearchParams(params).toString();
            const res = await fetch(url + '?' + qs);
            if (!res.ok) return [];
            return await res.json();
        } catch { return []; }
    }

    // ── Step 4 — passScore visibility ─────────
    function updatePassScoreVisibility() {
        document.getElementById('passScoreWrap').style.display =
            state.SURVEY_TYPE === 'QUIZ' ? '' : 'none';
        updateTotalPoints();
    }

    // ── Validate mỗi step ─────────────
    function validateStep(step) {
        if (step === 1) {
            const title = document.getElementById('fTitle').value.trim();
            if (!title) return 'Vui lòng nhập tiêu đề';
        }
        if (step === 2) {
            if (state.QUESTIONS.length === 0) return 'Cần có ít nhất 1 câu hỏi';
            for (let i = 0; i < state.QUESTIONS.length; i++) {
                const q = state.QUESTIONS[i];
                if (!q.QUESTION_TEXT.trim()) return `Câu #${i+1}: chưa có nội dung`;
                if (['SINGLE','MULTI','YESNO','DROPDOWN'].includes(q.QUESTION_TYPE)) {
                    const opts = q.OPTIONS.filter(o => o.OPTION_TEXT.trim());
                    if (opts.length < 2) return `Câu #${i+1}: cần ≥ 2 đáp án`;
                    if (state.SURVEY_TYPE === 'QUIZ' && opts.every(o => o.IS_CORRECT === 0))
                        return `Câu #${i+1} (QUIZ): chưa chọn đáp án đúng`;
                }
            }
        }
        if (step === 3) {
            if (state.RECIPIENT_MODE === 'DEPT_LINE_WORK') {
                const rows = document.querySelectorAll('#scopeDLWList .sa-dlw-row');
                let hasDept = false;
                rows.forEach(r => { if (r.querySelector('.dlw-dept').value) hasDept = true; });
                if (!hasDept) return 'Chưa thêm scope Dept/Line/Work nào';
            }
            if (state.RECIPIENT_MODE === 'EMPCD_LIST') {
                const list = parseEmpcdList();
                if (list.length === 0) return 'Chưa nhập mã thẻ nào';
            }
        }
        if (step === 4) {
            const start = document.getElementById('fStartDate').value;
            const end   = document.getElementById('fEndDate').value;
            if (!start || !end) return 'Vui lòng nhập cả ngày bắt đầu và kết thúc';
            if (end < start) return 'Ngày kết thúc phải >= ngày bắt đầu';
            if (state.SURVEY_TYPE === 'QUIZ') {
                const pass = Number(document.getElementById('fPassScore').value);
                if (!pass || pass <= 0) return 'QUIZ cần nhập PASS_SCORE > 0';
                const total = state.QUESTIONS.reduce((s, q) => s + (Number(q.POINTS) || 0), 0);
                if (pass > total) return `PASS_SCORE (${pass}) không được lớn hơn tổng POINTS (${total})`;
            }
        }
        return null;
    }

    function parseEmpcdList() {
        const raw = (document.getElementById('fEmpcdList').value || '').trim();
        return raw.split(/[\s,;]+/).filter(x => x.length > 0);
    }

    // ── Build payload ─────────────────
    function buildPayload() {
        // Scopes
        const scopes = [];
        if (state.RECIPIENT_MODE === 'DEPT_LINE_WORK' || state.RECIPIENT_MODE === 'MIXED') {
            document.querySelectorAll('#scopeDLWList .sa-dlw-row').forEach(r => {
                const dept = r.querySelector('.dlw-dept').value;
                if (!dept) return;
                scopes.push({
                    SCOPE_TYPE: 'DEPT_LINE_WORK',
                    DEPTCD:     dept,
                    LINECD:     r.querySelector('.dlw-line').value || null,
                    WORKCD:     r.querySelector('.dlw-work').value || null,
                });
            });
        }
        if (state.RECIPIENT_MODE === 'EMPCD_LIST' || state.RECIPIENT_MODE === 'MIXED') {
            parseEmpcdList().forEach(emp =>
                scopes.push({ SCOPE_TYPE: 'EMPCD', EMPCD: emp }));
        }

        // Questions order fix
        state.QUESTIONS.forEach((q, i) => {
            q.DISPLAY_ORDER = i + 1;
            q.OPTIONS.forEach((o, j) => o.DISPLAY_ORDER = j + 1);
        });

        return {
            ID:             E.id > 0 ? E.id : null,
            TITLE:          document.getElementById('fTitle').value.trim(),
            DESCRIPTION:    document.getElementById('fDescription').value.trim() || null,
            SURVEY_TYPE:    state.SURVEY_TYPE,
            LANG:           state.LANG,
            START_DATE:     document.getElementById('fStartDate').value || null,
            END_DATE:       document.getElementById('fEndDate').value   || null,
            RECIPIENT_MODE: state.RECIPIENT_MODE,
            PASS_SCORE:     state.SURVEY_TYPE === 'QUIZ'
                ? Number(document.getElementById('fPassScore').value) || null
                : null,
            QUESTIONS: state.QUESTIONS,
            SCOPES:    scopes,
        };
    }

    // ── Save Draft ─────────────────
    $btnDraft.addEventListener('click', async () => {
        for (let s = 1; s <= currentStep; s++) {
            if (isStepSkipped(s)) continue;
            const err = validateStep(s);
            if (err) { toast('Bước ' + s + ': ' + err, 'warning'); showStep(s); return; }
        }
        const r = await save();
        if (r?.success) {
            toast('Đã lưu nháp', 'success');
            setTimeout(() => { location.href = URLS.editBase + '?id=' + (r.id || E.id); }, 600);
        } else {
            toast(r?.message || 'Lưu thất bại', 'error');
        }
    });

    // ── Publish (stream progress) ─────────────────
    $btnPub.addEventListener('click', async () => {
        for (let s = 1; s <= totalSteps; s++) {
            if (isStepSkipped(s)) continue;
            const err = validateStep(s);
            if (err) { toast('Bước ' + s + ': ' + err, 'warning'); showStep(s); return; }
        }
        confirmDialog('Publish survey ngay? Danh sách người nhận sẽ được lưu vào hệ thống.', {
            title: 'Publish survey', okText: 'Publish', okClass: 'btn-primary',
            icon: 'cloud_upload', iconColor: '#4f46e5', iconBg: '#e0e7ff'
        }, async () => {
            const r = await save();
            if (!r?.success) { toast(r?.message || 'Lưu thất bại', 'error'); return; }
            const id = r.id || E.id;

            showPubModal();
            try {
                const res = await fetch(URLS.publishStream, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': TOKEN },
                    body: JSON.stringify({ Id: id, NewStatus: 'SCHEDULED' }),
                });
                if (!res.ok || !res.body) {
                    hidePubModal();
                    toast('Publish thất bại — data đã lưu nháp', 'error');
                    return;
                }

                const reader = res.body.getReader();
                const decoder = new TextDecoder();
                let buf = '';
                let finalOk = false, finalMsg = '';
                while (true) {
                    const { value, done } = await reader.read();
                    if (done) break;
                    buf += decoder.decode(value, { stream: true });
                    let idx;
                    while ((idx = buf.indexOf('\n')) >= 0) {
                        const line = buf.slice(0, idx).trim();
                        buf = buf.slice(idx + 1);
                        if (!line) continue;
                        try {
                            const ev = JSON.parse(line);
                            handlePubEvent(ev);
                            if (ev.phase === 'done') { finalOk = ev.success !== false; }
                            if (ev.phase === 'error') { finalOk = false; finalMsg = ev.message || ''; }
                        } catch {}
                    }
                }
                hidePubModal();
                if (finalOk) {
                    toast('Đã publish thành công', 'success');
                    setTimeout(() => { location.href = URLS.index; }, 600);
                } else {
                    toast(finalMsg || 'Publish thất bại — data đã lưu nháp', 'error');
                }
            } catch (err) {
                hidePubModal();
                toast('Lỗi kết nối khi publish', 'error');
            }
        });
    });

    function showPubModal() {
        const el = document.getElementById('pubProgressModal');
        if (!el) return;
        document.getElementById('pubPhase').textContent    = 'Đang publish survey...';
        document.getElementById('pubBar').style.width      = '0%';
        document.getElementById('pubCounter').textContent  = '';
        el.classList.add('show');
        el.style.display = 'flex';
    }
    function hidePubModal() {
        const el = document.getElementById('pubProgressModal');
        if (!el) return;
        el.classList.remove('show');
        el.style.display = 'none';
    }
    function handlePubEvent(ev) {
        const $phase = document.getElementById('pubPhase');
        const $bar   = document.getElementById('pubBar');
        const $cnt   = document.getElementById('pubCounter');
        if (ev.phase === 'schedule') {
            $phase.textContent = 'Đã lưu trạng thái, đang tạo danh sách người nhận...';
        } else if (ev.phase === 'snapshot') {
            const pct = ev.total > 0 ? Math.min(100, Math.round(ev.inserted * 100 / ev.total)) : 100;
            $phase.textContent = 'Đang lưu người nhận...';
            $bar.style.width   = pct + '%';
            $cnt.textContent   = ev.inserted + ' / ' + ev.total;
        } else if (ev.phase === 'done') {
            $phase.textContent = 'Hoàn tất!';
            $bar.style.width   = '100%';
            $cnt.textContent   = (ev.total || 0) + ' người nhận';
        } else if (ev.phase === 'error') {
            $phase.textContent = 'Lỗi: ' + (ev.message || '');
        }
    }

    async function save() {
        return await post(URLS.save, buildPayload());
    }
    async function post(url, body) {
        try {
            const res = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': TOKEN },
                body: JSON.stringify(body),
            });
            if (!res.ok) return null;
            return await res.json();
        } catch { return null; }
    }

    // ── utils ─────────────────
    function escapeAttr(s) {
        return String(s || '').replace(/&/g, '&amp;').replace(/"/g, '&quot;')
                              .replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    // ── Init ─────────────────
    showStep(1);
})();
