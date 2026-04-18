/**
 * Payslip User Logic
 * Handles viewing personal payslip
 */

$(document).ready(function () {
    $('#btnViewPayslip').on('click', function () {
        const periodId = $('#selectPeriod').val();
        if (!periodId) {
            AlertHelper.warn('Vui lòng chọn kỳ lương muốn xem');
            return;
        }

        viewMyPayslip(periodId);
    });

    function viewMyPayslip(periodId) {
        // Show loading
        $('#divPayslipDetail').hide();
        $('#divNoData').hide();
        
        const selectedOption = $('#selectPeriod').find(':selected');
        const periodName = selectedOption.text();
        const remark = selectedOption.data('remark');
        
        $('#hPeriodName').text(periodName);
        if (remark && remark.toString().trim()) {
            const lines = remark.toString().split(/\r\n|\n|\r/).filter(line => line.trim());
            const html = lines.map(line => $('<div>').text(line).html()).map(text => `<div>${text}</div>`).join('');
            $('#divRemarkText').html(html);
            $('#divRemark').show();
        } else {
            $('#divRemark').hide();
        }

        $.get('/Payslip/GetMyPayslip', { periodId: periodId }, function (res) {
            if (res.success && res.data && res.data.length > 0) {
                renderPayslip(res.data);
                $('#divPayslipDetail').fadeIn();
            } else {
                $('#divNoData').fadeIn();
            }
        });
    }

    function renderPayslip(items) {
        let incomeHtml = '';
        let deductHtml = '';
        let infoHtml = '';
        let thucLanh = 0;

        items.forEach(item => {
            if (item.IS_VISIBLE == 0) return;

            // Format gia tri
            let valStr = '-';
            if (item.AMOUNT !== null) {
                valStr = item.AMOUNT.toLocaleString() + ' VNĐ';
            } else if (item.TEXT_VALUE) {
                valStr = item.TEXT_VALUE;
            }

            const rowClass = (item.ITEM_CODE === 'THUC_LANH' || item.ITEM_CODE === 'TONG_CONG' || item.ITEM_CODE === 'TONG_KHAU_TRU') ? 'text-total' : '';

            const html = `
                <div class="payslip-row">
                    <span class="payslip-label ${rowClass}">${item.ITEM_NAME}</span>
                    <span class="payslip-value ${rowClass}">${valStr}</span>
                </div>
            `;

            if (item.ITEM_CODE === 'THUC_LANH') {
                thucLanh = item.AMOUNT || 0;
            }

            // Phan loai
            if (item.ITEM_TYPE === 'INCOME') {
                incomeHtml += html;
            } else if (item.ITEM_TYPE === 'DEDUCT') {
                deductHtml += html;
            } else if (item.ITEM_TYPE === 'INFO') {
                infoHtml += html;
            } else if (item.ITEM_TYPE === 'TOTAL') {
                // Total thi chia vao cac cot theo logic
                if (item.ITEM_CODE === 'TONG_CONG') incomeHtml += html;
                else if (item.ITEM_CODE === 'TONG_KHAU_TRU') deductHtml += html;
                else infoHtml += html;
            }
        });

        $('#hThucLanh').text(thucLanh.toLocaleString() + ' VNĐ');
        $('#divIncome').html(incomeHtml || '<p class="text-xs text-muted text-center py-3">Không có dữ liệu</p>');
        $('#divDeduct').html(deductHtml || '<p class="text-xs text-muted text-center py-3">Không có dữ liệu</p>');
        $('#divInfo').html(infoHtml || '<p class="text-xs text-muted text-center py-3">Không có dữ liệu</p>');
    }
});
