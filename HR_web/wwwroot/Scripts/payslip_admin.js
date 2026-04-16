/**
 * Payslip Admin Logic
 * Handles Import, Visibility and Period management
 */

const PAYSLIP_ITEMS = [
    'L_CB', 'L_BH_TC', 'NGAY_CONG', 'GIO_CONG', 'TONG_NGAY_LV', 'TIEN_NGAY_LV', 'TONG_CA_DEM', 'PC_CA_DEM',
    'GIO_TC_NGAY', 'L_TC_NGAY', 'GIO_TC_DEM', 'L_TC_DEM', 'GIO_TC_CN', 'L_TC_CN', 'GIO_TC_LE_NGAY', 'L_TC_LE_NGAY',
    'GIO_TC_LE_DEM', 'L_TC_LE_DEM', 'PC_DAC_BIET', 'PC_TRACH_NHIEM', 'THUONG_TRACH_NHIEM', 'PC_HUAN_LUYEN',
    'PC_KY_NANG', 'PC_THAM_NIEN', 'THUONG_CHUYEN_CAN', 'PC_DI_LAI', 'PC_CONG_VIEC', 'PC_CON_NHO', 'PC_PHU_NU',
    'PC_THU_HUT', 'PC_NHA_TRO', 'THUONG_ABC', 'THUONG_MOLD', 'PC_WD', 'THUONG_CNM', 'TRUY_LANH', 'KSK_HS_VEXE',
    'THUONG_GT_CNM', 'MUNG_CUOI_PD', 'KHAC_TIEN_BH', 'HOAN_THUE_2024', 'TONG_CONG', 'KT_BHXH', 'KT_BHYT',
    'KT_BHTN', 'KT_TNCN', 'KT_DOAN_PHI', 'SO_NGUOI_PT', 'INFO_NGUOI_PT', 'TONG_KHAU_TRU', 'THUC_LANH',
    'PN_DUNG_THANG', 'PN_DUNG_NAM', 'TONG_PN_2025', 'PN_2025_CON_LAI'
];

$(document).ready(function () {
    let currentPeriodId = null;
    let excelData = [];

    function parseExcelNumber(val) {
        if (val === undefined || val === null || val === "" || val === "-" || val === " - ") return null;
        if (typeof val === 'number') return val;
        if (typeof val === 'string') {
            // Remove commas and spaces
            let cleaned = val.replace(/,/g, '').replace(/\s/g, '');
            let n = parseFloat(cleaned);
            return isNaN(n) ? null : n;
        }
        return null;
    }

    // 1. Thay đổi kỳ lương
    $('#selectPeriod').on('change', function () {
        currentPeriodId = parseExcelNumber($(this).val());
        const isPublished = $(this).find(':selected').data('published') == 1;

        if (currentPeriodId) {
            $('#btnConfigColumns, #btnUploadExcel, #btnExportExcel, #btnRelease').prop('disabled', false);
            $('#divDataPreview').show();
            loadAdminData(1);
        } else {
            $('#btnConfigColumns, #btnUploadExcel, #btnExportExcel, #btnRelease').prop('disabled', true);
            $('#divDataPreview').hide();
        }

        if (isPublished) {
            $('#btnRelease').prop('disabled', true).text('Đã công bố');
        } else {
            $('#btnRelease').prop('disabled', false).html('<i class="fas fa-paper-plane me-1"></i> Công bố');
        }
    });

    // Mặc định ngày cho Modal Kỳ mới
    const now = new Date();
    const firstDay = new Date(now.getFullYear(), now.getMonth(), 1);
    const lastDay = new Date(now.getFullYear(), now.getMonth() + 1, 0);

    $('#txtDateFrom').val(firstDay.toISOString().substring(0, 10));
    $('#txtDateTo').val(lastDay.toISOString().substring(0, 10));

    // Toggle hẹn giờ
    $('#chkAutoPublish').on('change', function() {
        if ($(this).is(':checked')) {
            $('#divPublishTime').slideDown();
        } else {
            $('#divPublishTime').slideUp();
        }
    });

    // 2. Tạo kỳ lương mới
    $('#btnCreatePeriodSave').on('click', function () {
        const name = $('#txtPeriodName').val();
        const start = $('#txtDateFrom').val();
        const end = $('#txtDateTo').val();
        const isAutoPublish = $('#chkAutoPublish').is(':checked') ? 1 : 0;
        const publishDate = $('#txtPublishDate').val();
        const remark = $('#txtRemark').val();

        if (!name) { alert('Vui lòng nhập tên kỳ lương'); return; }
        if (isAutoPublish && !publishDate) { alert('Vui lòng chọn ngày giờ tự động công bố'); return; }

        const payload = { 
            periodName: name, 
            start: start, 
            end: end, 
            isAutoPublish: isAutoPublish, 
            publishDate: publishDate,
            remark: remark 
        };

        $.post('/Payslip/CreatePeriod', payload, function (res) {
            if (res.success) {
                location.reload();
            } else {
                alert(res.message);
            }
        });
    });

    // 3. Cấu hình hiển thị
    $('#btnConfigColumns').on('click', function () {
        $.get('/Payslip/GetItemsVisibility', { periodId: currentPeriodId }, function (res) {
            if (res.success) {
                let html = '';
                res.data.forEach(item => {
                    html += `
                        <tr>
                            <td class="ps-4">${item.DISPLAY_ORDER}</td>
                            <td>
                                <div class="d-flex flex-column">
                                    <h6 class="mb-0 text-sm">${item.ITEM_NAME}</h6>
                                    <p class="text-xs text-secondary mb-0">${item.ITEM_CODE}</p>
                                </div>
                            </td>
                            <td><span class="badge badge-sm bg-light text-dark border">${item.ITEM_TYPE}</span></td>
                            <td class="text-center">
                                <div class="form-check form-switch d-inline-block">
                                    <input class="form-check-input item-visibility-toggle" type="checkbox" 
                                           data-id="${item.ID}" ${item.IS_VISIBLE == 1 ? 'checked' : ''}>
                                </div>
                            </td>
                        </tr>
                    `;
                });
                $('#tbodyConfigItems').html(html);
                $('#modalConfigColumns').modal('show');
            }
        });
    });

    $('#btnSaveConfig').on('click', function () {
        const items = [];
        $('.item-visibility-toggle').each(function () {
            items.push({
                ID: $(this).data('id'),
                IS_VISIBLE: $(this).is(':checked') ? 1 : 0
            });
        });

        $.post('/Payslip/UpdateItemsVisibility', { periodId: currentPeriodId, items: items }, function (res) {
            if (res.success) {
                alert('Đã lưu cấu hình hiển thị');
                $('#modalConfigColumns').modal('hide');
            } else {
                alert(res.message);
            }
        });
    });

    // 4. Excel Import Logic
    $('#btnUploadExcel').on('click', function () {
        $('#fileInputExcel').val('');
        $('#theadExcelPreview, #tbodyExcelPreview').empty();
        $('#excelStatus').text('');
        $('#btnStartUpload').prop('disabled', true);
        $('#modalUploadExcel').modal('show');
    });

    $('#fileInputExcel').on('change', function (e) {
        const file = e.target.files[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = function (e) {
            const data = new Uint8Array(e.target.result);
            const workbook = XLSX.read(data, { type: 'array' });
            const firstSheet = workbook.Sheets[workbook.SheetNames[0]];
            const json = XLSX.utils.sheet_to_json(firstSheet, { header: 1 });

            if (json.length < 2) {
                alert('File Excel không đúng định dạng hoặc trống');
                return;
            }

            // Headers (Dòng 1)
            const headers = json[0];
            let headHtml = '<tr><th class="ps-2">EMPCD</th>';
            for (let i = 1; i < headers.length; i++) {
                headHtml += `<th>${headers[i] || 'N/A'}</th>`;
            }
            headHtml += '</tr>';
            $('#theadExcelPreview').html(headHtml);

            // Dữ liệu (Bắt đầu từ dòng 2)
            excelData = [];
            let bodyHtml = '';
            for (let i = 1; i < json.length; i++) {
                const row = json[i];
                if (!row[0]) continue; // Skip empty EMPCD

                const empCd = String(row[0]).trim();
                const values = [];
                const textValues = [];

                bodyHtml += `<tr><td class="ps-2 fw-bold text-primary">${empCd}</td>`;
                
                // Map cac cot con lai vao list 55 items
                for (let j = 0; j < 55; j++) {
                    const cellVal = row[j + 1]; // +1 vi o 0 la EmpCd
                    const numVal = parseExcelNumber(cellVal);
                    
                    if (numVal !== null) {
                        values.push(numVal);
                        textValues.push(null);
                        if (i <= 50) bodyHtml += `<td>${numVal.toLocaleString()}</td>`;
                    } else {
                        values.push(null);
                        const textVal = cellVal !== undefined && cellVal !== null ? String(cellVal).trim() : null;
                        textValues.push(textVal);
                        if (i <= 50) bodyHtml += `<td>${textVal || '-'}</td>`;
                    }
                }
                bodyHtml += '</tr>';

                excelData.push({
                    EmpCd: empCd,
                    Values: values,
                    TextValues: textValues
                });

                if (i === 50) {
                    bodyHtml += '<tr><td colspan="100" class="text-center text-muted">... chỉ hiển thị 50 dòng nháp ...</td></tr>';
                }
            }
            if (json.length <= 50) $('#tbodyExcelPreview').html(bodyHtml);
            else $('#tbodyExcelPreview').html(bodyHtml); // Re-set anyway

            $('#excelStatus').text(`Đã đọc ${excelData.length} nhân viên. Sẵn sàng Import.`);
            $('#btnStartUpload').prop('disabled', false);
        };
        reader.readAsArrayBuffer(file);
    });

    $('#btnStartUpload').on('click', function () {
        if (!confirm('Hệ thống sẽ xóa dữ liệu cũ của kỳ này và ghi đè dữ liệu mới. Bạn chắc chứ?')) return;

        const btn = $(this);
        btn.prop('disabled', true).html('<span class="spinner-border spinner-border-sm me-1"></span> Đang xử lý...');

        const batchSize = 200;
        let processedCount = 0;

        async function uploadNextBatch() {
            const batch = excelData.slice(processedCount, processedCount + batchSize);
            if (batch.length === 0) {
                alert('Hoàn tất Import dữ liệu cho ' + processedCount + ' nhân viên!');
                location.reload();
                return;
            }

            try {
                const res = await $.ajax({
                    url: '/Payslip/UploadData',
                    type: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify({
                        periodId: currentPeriodId,
                        data: batch,
                        isFirstBatch: processedCount === 0 
                    })
                });

                if (res.success) {
                    processedCount += batch.length;
                    $('#excelStatus').text(`Đang Import: ${processedCount}/${excelData.length}...`);
                    uploadNextBatch();
                } else {
                    alert('Lỗi: ' + res.message);
                    btn.prop('disabled', false).html('<i class="fas fa-check me-1"></i> Import Lại');
                }
            } catch (err) {
                alert('Lỗi kết nối server: ' + err.statusText);
                btn.prop('disabled', false).html('<i class="fas fa-check me-1"></i> Import Lại');
            }
        }

        uploadNextBatch();
    });

    // 5. Load data hien tai (Server side paging + search)
    let curPage = 1;
    let maxPage = 1;
    const pageSize = 100;

    function loadAdminData(page) {
        curPage = page;
        const search = $('#txtSearchEmp').val().trim();

        $('#tbodyAdminData').html('<tr><td colspan="3" class="text-center py-4"><div class="spinner-border text-primary spinner-border-sm"></div> Đang tải...</td></tr>');

        $.get('/Payslip/GetAdminList', { 
            periodId: currentPeriodId, 
            search: search,
            page: page, 
            pageSize: pageSize 
        }, function (res) {
            if (res.success) {
                $('#spanTotalRecords').text(res.total);
                maxPage = Math.max(1, Math.ceil(res.total / pageSize));

                let html = '';
                if (res.data.length === 0) {
                    html = '<tr><td colspan="3" class="text-center text-muted py-4">Không tìm thấy dữ liệu</td></tr>';
                } else {
                    res.data.forEach(row => {
                        const thucLanh = row.Details.find(x => x.ITEM_CODE === 'THUC_LANH')?.AMOUNT || 0;
                        html += `
                            <tr>
                                <td class="ps-4">
                                    <div class="d-flex flex-column">
                                        <h6 class="mb-0 text-sm">${row.EMP_NAME || 'Unknown'}</h6>
                                        <p class="text-xs text-secondary mb-0">${row.EMPCD}</p>
                                    </div>
                                </td>
                                <td class="text-center font-weight-bold">
                                    <span class="text-dark">${thucLanh.toLocaleString()} VNĐ</span>
                                </td>
                                <td class="text-center">
                                    <button class="btn btn-link text-info text-gradient px-3 mb-0 btn-view-detail" 
                                            data-empcd="${row.EMPCD}">
                                        <i class="fas fa-eye me-2"></i> Chi tiết
                                    </button>
                                </td>
                            </tr>
                        `;
                    });
                }
                $('#tbodyAdminData').html(html);
                renderPagination();
            }
        });
    }

    function renderPagination() {
        const $ul = $('#ulPagination').empty();
        const start = (curPage - 1) * pageSize + 1;
        const total = parseInt($('#spanTotalRecords').text());
        const end = Math.min(curPage * pageSize, total);

        $('#divPnlInfo').html(`Hiển thị <b>${start}-${end}</b> trong số <b>${total}</b> nhân viên`);

        const mkBtn = (label, p, disabled, active) => {
            const li = $('<li class="page-item">').toggleClass('disabled',!!disabled).toggleClass('active',!!active);
            $('<a class="page-link" href="javascript:;">').text(label).on('click', () => { if(!disabled && !active) loadAdminData(p); }).appendTo(li);
            return li;
        };

        $ul.append(mkBtn('«', 1, curPage <= 1));
        $ul.append(mkBtn('‹', curPage - 1, curPage <= 1));

        let from = Math.max(1, curPage - 2), to = Math.min(maxPage, curPage + 2);
        for(let p = from; p <= to; p++) $ul.append(mkBtn(p, p, false, p === curPage));

        $ul.append(mkBtn('›', curPage + 1, curPage >= maxPage));
        $ul.append(mkBtn('»', maxPage, curPage >= maxPage));
    }

    $('#txtSearchEmp').on('input', debounce(() => loadAdminData(1), 500));

    // 6. Xuất Excel Full
    $('#btnExportExcel').on('click', function () {
        const btn = $(this);
        const originalHtml = btn.html();
        btn.prop('disabled', true).html('<span class="spinner-border spinner-border-sm me-1"></span> Đang tải...');

        $.get('/Payslip/GetExportData', { periodId: currentPeriodId }, function (res) {
            if (res.success && res.data.length > 0) {
                const data = [];
                const headers = ['STT', 'Mã NV', 'Họ Tên'];
                const itemKeys = res.data[0].Details.map(d => d.ITEM_CODE);
                const itemNames = res.data[0].Details.map(d => d.ITEM_NAME);
                
                headers.push(...itemNames);
                data.push(headers);

                res.data.forEach((emp, index) => {
                    const row = [index + 1, emp.EMPCD, emp.EMP_NAME];
                    itemKeys.forEach(key => {
                        const d = emp.Details.find(x => x.ITEM_CODE === key);
                        row.push(d?.AMOUNT ?? d?.TEXT_VALUE ?? '');
                    });
                    data.push(row);
                });

                const ws = XLSX.utils.aoa_to_sheet(data);
                const wb = XLSX.utils.book_new();
                XLSX.utils.book_append_sheet(wb, ws, "Payslip_Data");
                XLSX.writeFile(wb, `Payslip_Export_${currentPeriodId}.xlsx`);
            } else {
                alert('Không có dữ liệu để xuất');
            }
        }).always(() => {
            btn.prop('disabled', false).html(originalHtml);
        });
    });

    // 7. Công bố
    $('#btnRelease').on('click', function () {
        if (!confirm('Sau khi công bộ, tất cả nhân viên sẽ thấy phiếu lương này. Bạn chắc chứ?')) return;
        $.post('/Payslip/ReleasePeriod', { id: currentPeriodId }, function (res) {
            if (res.success) {
                alert(res.message);
                location.reload();
            }
        });
    });

    function debounce(fn, d) { let t; return function(){ clearTimeout(t); t=setTimeout(fn,d); }; }
});
