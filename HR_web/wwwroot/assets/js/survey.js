// ══════════════════════════════════════════════════════════════
// SURVEY — làm survey 1 câu / 1 màn hình, auto-save khi Next.
// Đọc dữ liệu từ window.SURVEY_DATA (Do.cshtml inject sẵn).
// ══════════════════════════════════════════════════════════════
(function () {
    const D = window.SURVEY_DATA || {};
    if (!D.questions || D.questions.length === 0) return;

    const isEn      = D.lang === 'EN';
    const isQuiz    = D.type === 'QUIZ';
    const mode      = D.mode || 'do';    // 'do' = user thật | 'preview' | 'test'
    const t = {
        next:       isEn ? 'Next'       : 'Tiếp theo',
        submit:     isEn ? 'Submit'     : 'Gửi',
        required:   isEn ? 'This question is required'  : 'Câu hỏi bắt buộc',
        selectHint: isEn ? 'Select one option'          : 'Chọn 1 đáp án',
        multiHint:  isEn ? 'Select all that apply'      : 'Chọn tất cả đáp án đúng',
        ratingHint: isEn ? 'Tap a star to rate'         : 'Chạm vào sao để đánh giá',
        textHint:   isEn ? 'Type your answer (max 1000)': 'Nhập câu trả lời (tối đa 1000)',
        thankPoll:  isEn ? 'Thank you for participating.' : 'Cảm ơn bạn đã tham gia khảo sát.',
        thankSub:   isEn ? 'Your response has been recorded successfully.' : 'Ý kiến của bạn đã được ghi nhận thành công.',
        thankTitle: isEn ? 'Thank you!' : 'Cảm ơn bạn!',
        pass:       isEn ? 'Congratulations, you passed!' : 'Xuất sắc! Bạn đã hoàn thành bài quiz',
        fail:       isEn ? 'Not quite, please try again' : 'Chưa đạt, cố gắng lần sau nhé!',
        passSub:    isEn ? 'Great job on completing the quiz.' : 'Chúc mừng bạn đã vượt qua bài kiểm tra!',
        failSub:    isEn ? 'Please review the material and retry when possible.' : 'Đừng nản, ôn tập lại và thử lại lần sau bạn nhé.',
        yourScore:  isEn ? 'Your score' : 'Điểm của bạn',
        saveErr:    isEn ? 'Save failed. Please try again.' : 'Lưu không thành công. Vui lòng thử lại.',
        submitErr:  isEn ? 'Submit failed. Please try again.' : 'Gửi không thành công. Vui lòng thử lại.',
    };

    // ── State ─────────────────────────────────────────
    let currentIdx = 0;
    // answers: Map<questionId, {optionIds?: number[], text?: string, number?: number}>
    const answers = new Map();
    (D.answers || []).forEach(a => {
        const val = {};
        if (a.ANSWER_OPTION_IDS) val.optionIds = a.ANSWER_OPTION_IDS.split(',').map(x => parseInt(x, 10)).filter(x => !isNaN(x));
        if (a.ANSWER_TEXT)       val.text     = a.ANSWER_TEXT;
        if (a.ANSWER_NUMBER != null) val.number = Number(a.ANSWER_NUMBER);
        answers.set(a.QUESTION_ID, val);
    });

    // Resume: skip tới câu đầu tiên chưa trả lời
    const firstUnansweredIdx = D.questions.findIndex(q => !hasAnswer(q));
    if (firstUnansweredIdx > 0) currentIdx = firstUnansweredIdx;
    else if (firstUnansweredIdx === -1) currentIdx = D.questions.length - 1;

    // ── DOM refs ─────────────────────────────────────
    const $wrapper       = document.getElementById('questionWrapper');
    const $progress      = document.getElementById('progressBar');
    const $progressText  = document.getElementById('progressText');
    const $btnBack       = document.getElementById('btnBack');
    const $btnNext       = document.getElementById('btnNext');
    const $btnNextLabel  = document.getElementById('btnNextLabel');
    const $btnIlliterate = document.getElementById('btnIlliterate');
    const $token         = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]');

    // ── Helpers ─────────────────────────────────────
    function hasAnswer(q) {
        const v = answers.get(q.ID);
        if (!v) return false;
        if (v.optionIds && v.optionIds.length > 0) return true;
        if (v.text && v.text.length > 0) return true;
        if (v.number != null) return true;
        return false;
    }

    function isAnswerValid(q) {
        if (q.IS_REQUIRED !== 1) return true;
        return hasAnswer(q);
    }

    function escapeHtml(s) {
        return String(s || '')
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    // ── Render 1 câu ─────────────────────────────────
    function render() {
        const q = D.questions[currentIdx];
        const v = answers.get(q.ID) || {};

        const requiredMark = q.IS_REQUIRED === 1
            ? '<span class="sv-question-required">*</span>' : '';

        let hintText = '';
        let bodyHtml = '';

        switch (q.QUESTION_TYPE) {
            case 'SINGLE':
            case 'DROPDOWN':
                hintText = t.selectHint;
                if (q.QUESTION_TYPE === 'DROPDOWN') {
                    bodyHtml = renderDropdown(q, v);
                } else {
                    bodyHtml = renderOptions(q, v, false);
                }
                break;
            case 'YESNO':
                hintText = t.selectHint;
                bodyHtml = renderOptions(q, v, false);
                break;
            case 'MULTI':
                hintText = t.multiHint;
                bodyHtml = renderOptions(q, v, true);
                break;
            case 'RATING':
                hintText = t.ratingHint;
                bodyHtml = renderRating(q, v);
                break;
            case 'TEXT':
                hintText = t.textHint;
                bodyHtml = renderTextarea(q, v);
                break;
        }

        $wrapper.innerHTML = `
            <div class="sv-question" data-qid="${q.ID}">
                <div class="sv-question-text">${escapeHtml(q.QUESTION_TEXT)}${requiredMark}</div>
                <div class="sv-question-hint">${hintText}</div>
                ${bodyHtml}
            </div>`;

        wireQuestionEvents(q);
        updateProgress();
        updateFooter();
    }

    function renderOptions(q, v, multi) {
        const selected = new Set(v.optionIds || []);
        const rows = (q.OPTIONS || []).map(o => {
            const isActive = selected.has(o.ID) ? 'active' : '';
            return `
                <div class="sv-option ${isActive}" data-oid="${o.ID}" data-multi="${multi ? 1 : 0}">
                    <div class="sv-option-marker"></div>
                    <div class="sv-option-text">${escapeHtml(o.OPTION_TEXT)}</div>
                </div>`;
        }).join('');
        return `<div class="sv-options" data-multi="${multi ? 1 : 0}">${rows}</div>`;
    }

    function renderDropdown(q, v) {
        const selected = (v.optionIds && v.optionIds[0]) || null;
        const opts = (q.OPTIONS || []).map(o =>
            `<option value="${o.ID}" ${selected === o.ID ? 'selected' : ''}>${escapeHtml(o.OPTION_TEXT)}</option>`
        ).join('');
        return `<select class="sv-dropdown"><option value="">--- ${isEn ? 'Select' : 'Chọn'} ---</option>${opts}</select>`;
    }

    function renderRating(q, v) {
        const val = v.number || 0;
        const stars = [1, 2, 3, 4, 5].map(i =>
            `<i class="bi bi-star-fill sv-star ${i <= val ? 'filled' : ''}" data-val="${i}"></i>`
        ).join('');
        return `<div class="sv-rating">${stars}</div>
                <div class="sv-rating-label">${val ? val + ' / 5' : ''}</div>`;
    }

    function renderTextarea(q, v) {
        const text = v.text || '';
        return `
            <textarea class="sv-textarea" maxlength="1000"
                      placeholder="${isEn ? 'Your answer...' : 'Câu trả lời của bạn...'}">${escapeHtml(text)}</textarea>
            <div class="sv-char-count"><span class="cur">${text.length}</span>/1000</div>`;
    }

    // ── Wire event handlers cho từng loại ─────────
    function wireQuestionEvents(q) {
        const type = q.QUESTION_TYPE;

        if (type === 'SINGLE' || type === 'YESNO') {
            $wrapper.querySelectorAll('.sv-option').forEach(el => {
                el.addEventListener('click', () => {
                    $wrapper.querySelectorAll('.sv-option').forEach(x => x.classList.remove('active'));
                    el.classList.add('active');
                    answers.set(q.ID, { optionIds: [parseInt(el.dataset.oid, 10)] });
                    updateFooter();
                });
            });
        }
        else if (type === 'MULTI') {
            $wrapper.querySelectorAll('.sv-option').forEach(el => {
                el.addEventListener('click', () => {
                    el.classList.toggle('active');
                    const ids = Array.from($wrapper.querySelectorAll('.sv-option.active'))
                        .map(x => parseInt(x.dataset.oid, 10));
                    answers.set(q.ID, { optionIds: ids });
                    updateFooter();
                });
            });
        }
        else if (type === 'DROPDOWN') {
            const $sel = $wrapper.querySelector('.sv-dropdown');
            $sel.addEventListener('change', () => {
                const id = parseInt($sel.value, 10);
                if (id) answers.set(q.ID, { optionIds: [id] });
                else answers.delete(q.ID);
                updateFooter();
            });
        }
        else if (type === 'RATING') {
            $wrapper.querySelectorAll('.sv-star').forEach(el => {
                el.addEventListener('click', () => {
                    const val = parseInt(el.dataset.val, 10);
                    answers.set(q.ID, { number: val });
                    render();       // re-render để cập nhật star filled
                });
            });
        }
        else if (type === 'TEXT') {
            const $ta    = $wrapper.querySelector('.sv-textarea');
            const $count = $wrapper.querySelector('.sv-char-count .cur');
            const $wrap  = $wrapper.querySelector('.sv-char-count');
            $ta.addEventListener('input', () => {
                const val = $ta.value;
                $count.textContent = val.length;
                if (val.length > 950) $wrap.classList.add('warn');
                else $wrap.classList.remove('warn');
                if (val.length > 0) answers.set(q.ID, { text: val });
                else answers.delete(q.ID);
                updateFooter();
            });
        }
    }

    // ── Progress + footer ─────────────────────────
    function updateProgress() {
        const pct = Math.round(((currentIdx + 1) / D.questions.length) * 100);
        $progress.style.width = pct + '%';
        $progressText.textContent = `${currentIdx + 1} / ${D.questions.length}`;
    }
    function updateFooter() {
        const q = D.questions[currentIdx];
        $btnBack.disabled = currentIdx === 0;
        const isLast = currentIdx === D.questions.length - 1;
        $btnNextLabel.textContent = isLast ? t.submit : t.next;
        $btnNext.disabled = !isAnswerValid(q);
    }

    // ── Navigation ─────────────────────────────
    $btnBack.addEventListener('click', () => {
        if (currentIdx === 0) return;
        currentIdx--;
        render();
    });
    $btnNext.addEventListener('click', async () => {
        const q = D.questions[currentIdx];
        if (!isAnswerValid(q)) return;

        // Auto-save chỉ ở mode 'do'
        $btnNext.disabled = true;
        if (mode === 'do') {
            const ok = await saveAnswer(q);
            if (!ok) { alert(t.saveErr); $btnNext.disabled = false; return; }
        } else {
            $btnNext.disabled = false;
        }

        if (currentIdx === D.questions.length - 1) {
            if (mode === 'preview') showResult(null);  // show thank-you & thoát
            else openModal('modalSubmit');
        } else {
            currentIdx++;
            render();
        }
    });

    // ── AJAX ─────────────────────────────
    async function post(url, payload) {
        const res = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': $token?.value || '',
            },
            body: JSON.stringify(payload),
        });
        if (!res.ok) return null;
        try { return await res.json(); } catch { return null; }
    }

    async function saveAnswer(q) {
        const v = answers.get(q.ID) || {};
        const payload = {
            surveyId:        D.surveyId,
            questionId:      q.ID,
            answerOptionIds: v.optionIds ? v.optionIds.join(',') : null,
            answerText:      v.text || null,
            answerNumber:    v.number || null,
        };
        const r = await post('/Survey/SaveAnswer', payload);
        return r && r.success === true;
    }

    async function submitSurvey() {
        if (mode === 'test') {
            const payload = {
                SURVEY_ID: D.surveyId,
                ANSWERS: D.questions.map(q => {
                    const v = answers.get(q.ID) || {};
                    return {
                        QUESTION_ID:       q.ID,
                        ANSWER_OPTION_IDS: v.optionIds ? v.optionIds.join(',') : null,
                        ANSWER_TEXT:       v.text   || null,
                        ANSWER_NUMBER:     v.number || null,
                    };
                }),
            };
            const r = await post('/SurveyAdmin/TestSubmit', payload);
            return r || { success: false };
        }
        const r = await post('/Survey/Submit', { surveyId: D.surveyId });
        return r || { success: false };
    }

    async function skipIlliterate() {
        const r = await post('/Survey/Skip', { surveyId: D.surveyId });
        return r || { success: false };
    }

    // ── Modal open/close ─────────────────────────────
    function openModal(id)  { document.getElementById(id).classList.add('show'); }
    function closeModal(id) { document.getElementById(id).classList.remove('show'); }
    document.querySelectorAll('[data-close]').forEach(el => {
        el.addEventListener('click', () => closeModal(el.dataset.close));
    });

    // ── Submit modal actions ─────────────────────
    const $btnSubmitConfirm = document.getElementById('btnSubmitConfirm');
    if ($btnSubmitConfirm) {
        $btnSubmitConfirm.addEventListener('click', async () => {
            $btnSubmitConfirm.disabled = true;
            const r = await submitSurvey();
            $btnSubmitConfirm.disabled = false;
            closeModal('modalSubmit');
            if (!r.success) { alert(r.message || t.submitErr); return; }
            showResult(r.data);
        });
    }

    // ── Landing: nhấn Bắt đầu mới vô làm ─────────────
    const $landing   = document.getElementById('surveyLanding');
    const $container = document.getElementById('surveyContainer');
    const $btnStart  = document.getElementById('btnStartSurvey');
    const $btnIllLanding = document.getElementById('btnIlliterateLanding');
    if ($btnStart && $landing && $container) {
        $btnStart.addEventListener('click', () => {
            $landing.style.display = 'none';
            $container.classList.remove('d-none');
        });
    }
    if ($btnIllLanding) {
        $btnIllLanding.addEventListener('click', () => openModal('modalIlliterate'));
    }

    // ── Illiterate modal actions (chỉ mode 'do') ─────────
    if ($btnIlliterate) {
        $btnIlliterate.addEventListener('click', () => openModal('modalIlliterate'));
    }
    const $btnIlliterateConfirm = document.getElementById('btnIlliterateConfirm');
    if ($btnIlliterateConfirm) {
        $btnIlliterateConfirm.addEventListener('click', async () => {
            $btnIlliterateConfirm.disabled = true;
            const r = await skipIlliterate();
            $btnIlliterateConfirm.disabled = false;
            closeModal('modalIlliterate');
            if (!r.success) { alert(r.message || t.submitErr); return; }
            window.location.href = '/Home/Index';
        });
    }

    // ── Result modal (sau submit) ─────────────
    function showResult(data) {
        const $title = document.getElementById('resultTitle');
        const $body  = document.getElementById('resultBody');
        // Preview mode: không có modal → alert cảm ơn
        if (!$title || !$body) {
            alert(t.thankTitle + ' — ' + t.thankPoll);
            window.location.href = '/SurveyAdmin/Index';
            return;
        }

        const mascot = document.getElementById('surveyContainer')?.dataset.mascot || '';
        const mascotImg = mascot ? `<div class="sv-congrats-mascot"><img src="${mascot}" alt="mascot" onerror="this.parentElement.style.display='none'"/></div>` : '';

        if (isQuiz && data) {
            const score = data.SCORE ?? 0;
            const max   = data.MAX_SCORE ?? 0;
            const pass  = data.IS_PASS === 1;
            $title.textContent = '';
            $body.innerHTML = `
                <div class="sv-congrats">
                    ${mascotImg}
                    <div class="sv-congrats-title ${pass ? '' : 'fail'}">${pass ? t.pass : t.fail}</div>
                    <div class="sv-congrats-sub">${pass ? t.passSub : t.failSub}</div>
                    <div class="sv-congrats-score">
                        <span class="sv-congrats-score-value">${score}</span>
                        <span class="sv-congrats-score-max">/ ${max}</span>
                    </div>
                </div>`;
        } else {
            $title.textContent = '';
            $body.innerHTML = `
                <div class="sv-congrats">
                    ${mascotImg}
                    <div class="sv-congrats-title">${t.thankTitle}</div>
                    <div class="sv-congrats-sub">${t.thankPoll}<br/><span class="text-muted">${t.thankSub}</span></div>
                </div>`;
        }
        openModal('modalResult');
    }

    // ── Kickoff ─────────────────────────────
    render();
})();
