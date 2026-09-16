// Popup "món tên giống nhau" — dùng chung cho Manage.cshtml (Import Excel thực đơn tuần)
// và FoodManage.cshtml (Thêm món thủ công + Import Excel danh mục món ăn).
// item: { typedName, matchedName, matchedId, matchedHasImage, extraLabel? }
// onChoose(decision): decision = 'useExisting' | 'createNew' | 'createNewCopyImage'
function showSimilarFoodModal(item, onChoose) {
    const oldEl = document.getElementById('similarFoodModal');
    if (oldEl) oldEl.remove();

    const imgHtml = item.matchedHasImage
        ? `<img src="${GET_FOOD_IMAGE_URL_SHARED}?fileName=${item.matchedId}.jpg" style="width:70px;height:70px;object-fit:cover;border-radius:8px;" onerror="this.style.display='none';" />`
        : `<div style="width:70px;height:70px;background:#f0f0f0;border-radius:8px;display:flex;align-items:center;justify-content:center;"><i class="bi bi-image text-secondary"></i></div>`;

    const wrap = document.createElement('div');
    wrap.innerHTML = `
    <div class="modal fade" id="similarFoodModal" tabindex="-1" data-bs-backdrop="static">
        <div class="modal-dialog">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title"><i class="bi bi-exclamation-diamond text-warning me-1"></i> Tên món gần giống món đã có</h5>
                </div>
                <div class="modal-body">
                    ${item.extraLabel ? `<div class="text-xs text-secondary mb-2">${item.extraLabel}</div>` : ''}
                    <p class="mb-2">Bạn nhập <strong>"${item.typedName}"</strong> — gần giống món đã có trong danh mục:</p>
                    <div class="d-flex align-items-center gap-2 mb-3 p-2" style="background:#f8fafc;border-radius:8px;">
                        ${imgHtml}
                        <div>
                            <div class="fw-bold">${item.matchedName}</div>
                            <div class="text-xs text-secondary">${item.matchedHasImage ? 'Đã có hình' : 'Chưa có hình'}</div>
                        </div>
                    </div>
                    <div class="d-grid gap-2">
                        <button type="button" class="btn btn-success btn-sm" data-choice="useExisting">
                            <i class="bi bi-check2-circle me-1"></i> Dùng lại "${item.matchedName}" (có sẵn)
                        </button>
                        <button type="button" class="btn btn-outline-dark btn-sm" data-choice="createNew">
                            <i class="bi bi-plus-circle me-1"></i> Vẫn tạo món mới, không có hình
                        </button>
                        <button type="button" class="btn btn-outline-primary btn-sm" data-choice="createNewCopyImage">
                            <i class="bi bi-images me-1"></i> Tạo món mới + copy hình từ "${item.matchedName}"
                        </button>
                    </div>
                </div>
            </div>
        </div>
    </div>`;
    document.body.appendChild(wrap.firstElementChild);

    const modalEl = document.getElementById('similarFoodModal');
    const modal = new bootstrap.Modal(modalEl);
    modalEl.querySelectorAll('[data-choice]').forEach(btn => {
        btn.addEventListener('click', () => {
            modal.hide();
            onChoose(btn.dataset.choice);
        });
    });
    modalEl.addEventListener('hidden.bs.modal', () => modalEl.remove());
    modal.show();
}

// Chạy tuần tự popup cho từng item trong 1 danh sách pending, gọi callback(results) khi xong hết.
// results: mảng cùng thứ tự items, mỗi phần tử { ...item, decision }
function resolveSimilarFoodQueue(items, onDone) {
    const results = [];
    function next(i) {
        if (i >= items.length) { onDone(results); return; }
        showSimilarFoodModal(items[i], (decision) => {
            results.push({ ...items[i], decision });
            next(i + 1);
        });
    }
    next(0);
}
