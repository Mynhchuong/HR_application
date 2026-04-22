/**
 * Payslip User Logic
 * Handles viewing personal payslip with premium UI
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
        // Reset and hide
        $('#divPayslipDetail').hide();
        $('#divNoData').hide();
        
        const selectedOption = $('#selectPeriod').find(':selected');
        const periodName = selectedOption.text();
        
        $('#hPeriodName').text(periodName);

        $.get('/Payslip/GetMyPayslip', { periodId: periodId }, function (res) {
            if (res.success && res.data && res.data.length > 0) {
                const hasData = renderPayslip(res.data);
                if (hasData) {
                    $('#divPayslipDetail').fadeIn(400);
                } else {
                    $('#divNoData').fadeIn();
                }
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

            // Simple value formatting
            let valStr = '-';
            if (item.AMOUNT !== null) {
                valStr = item.AMOUNT.toLocaleString();
            } else if (item.TEXT_VALUE) {
                valStr = item.TEXT_VALUE;
            }

            // Append units (Prioritize database UNIT, fallback to hardcoded logic)
            if (item.UNIT) {
                valStr += ' ' + item.UNIT;
            } else if (item.AMOUNT !== null && valStr !== '-') {
                const nameLower = (item.ITEM_NAME || '').toLowerCase();
                const code = item.ITEM_CODE || '';

                if (item.ITEM_TYPE === 'INCOME' || item.ITEM_TYPE === 'DEDUCT' || code === 'THUC_LANH' || code === 'TONG_CONG' || code === 'TONG_KHAU_TRU') {
                    valStr += ' VNĐ';
                } else if (nameLower.includes('tăng ca') || nameLower.includes('giờ')) {
                    valStr += ' giờ';
                } else if (nameLower.includes('pn') || nameLower.includes('ngày') || nameLower.includes('phép')) {
                    valStr += ' ngày';
                } else if (nameLower.includes('người phụ thuộc')) {
                    valStr += ' người';
                }
            }

            // Highlighting logic
            let labelClass = '';
            let valueClass = 'fw-bold text-dark'; // Always bold values for better visibility
            
            if (item.ITEM_CODE === 'TONG_CONG') {
                labelClass = 'fw-bold text-dark';
                valueClass = 'text-total-income fw-bold';
            } else if (item.ITEM_CODE === 'TONG_KHAU_TRU') {
                labelClass = 'fw-bold text-dark';
                valueClass = 'text-total-deduct fw-bold';
            }

            const html = `
                <div class="payslip-row">
                    <span class="payslip-label ${labelClass}">${item.ITEM_NAME}</span>
                    <span class="payslip-value ${valueClass}">${valStr}</span>
                </div>
            `;

            if (item.ITEM_CODE === 'THUC_LANH') {
                thucLanh = item.AMOUNT || 0;
            }

            // Categorize items
            if (item.ITEM_TYPE === 'INCOME') {
                incomeHtml += html;
            } else if (item.ITEM_TYPE === 'DEDUCT') {
                deductHtml += html;
            } else if (item.ITEM_TYPE === 'INFO') {
                infoHtml += html;
            } else if (item.ITEM_TYPE === 'TOTAL') {
                if (item.ITEM_CODE === 'TONG_CONG') incomeHtml += html;
                else if (item.ITEM_CODE === 'TONG_KHAU_TRU') deductHtml += html;
                else infoHtml += html;
            }
        });

        // Update Summary Section
        $('#hThucLanh').text(thucLanh.toLocaleString());
        
        // Inject sections
        $('#divIncome').html(incomeHtml || '<div class="text-center py-3 text-muted small">Không có dữ liệu</div>');
        $('#divDeduct').html(deductHtml || '<div class="text-center py-3 text-muted small">Không có dữ liệu</div>');
        $('#divInfo').html(infoHtml || '<div class="text-center py-3 text-muted small">Không có dữ liệu</div>');

        return thucLanh > 0 || incomeHtml !== '' || deductHtml !== '';
    }
});
