/**
 * TimePickerHelper — dropdown chọn giờ/phút có icon + buổi trong ngày (Đêm/Sáng/Trưa/Chiều/Tối),
 * thay cho input[type=time] trần. Tách ra dùng chung sau khi thấy lặp lại giữa Gate Pass và
 * Attendance Confirm/WorkerForm (yêu cầu 2026-09-22).
 *
 * Markup cần có sẵn cho mỗi "prefix" (VD prefix = "wfTimeIn"):
 *   <select id="wfTimeInHour"></select>
 *   <select id="wfTimeInMin"></select>
 *   <input type="hidden" id="wfTimeIn" />   -- lưu giá trị "HH:mm", đọc/ghi qua $('#wfTimeIn').val()
 *
 * Usage:
 *   TimePickerHelper.init('wfTimeIn');                       // build options + wire change events
 *   TimePickerHelper.setValue('wfTimeIn', '07:30', isAuto);  // set giá trị (isAuto = true để tô màu gợi ý tự động)
 *   TimePickerHelper.getValue('wfTimeIn');                   // đọc lại "HH:mm" hiện tại
 *   TimePickerHelper.setRange('wfTimeIn', { min: '07:30', max: null });  // giới hạn theo ca làm việc
 *
 * CSS đi kèm: ~/css/time-picker.css (class .time-picker, .hour-select, .min-select, .auto-time).
 */
(function (window, document) {
    'use strict';

    var _ranges = {}; // prefix -> {min, max} đang áp dụng — cần nhớ lại để lọc phút mỗi khi đổi giờ

    function hourBand(h) {
        if (h <= 4)  return { icon: '🌙', label: 'Đêm',   color: '#4c1d95' };
        if (h <= 10) return { icon: '🌅', label: 'Sáng',  color: '#c2410c' };
        if (h <= 12) return { icon: '☀️', label: 'Trưa',  color: '#b45309' };
        if (h <= 17) return { icon: '🌤️', label: 'Chiều', color: '#0369a1' };
        return             { icon: '🌆', label: 'Tối',   color: '#1e293b' };
    }

    function makeHourOption(h) {
        var band = hourBand(h);
        var hh = String(h).padStart(2, '0');
        var o = document.createElement('option');
        o.value = hh;
        o.text  = band.icon + ' ' + hh + 'h ' + band.label;
        o.style.color = band.color;
        return o;
    }

    function el(prefix, suffix) { return document.getElementById(prefix + suffix); }

    // min/max: "HH:mm" hoặc null/undefined = không giới hạn phía đó
    function withinRange(h, m, min, max) {
        if (min) {
            var minP = min.split(':'); var minH = parseInt(minP[0], 10), minM = parseInt(minP[1], 10);
            if (h < minH || (h === minH && m < minM)) return false;
        }
        if (max) {
            var maxP = max.split(':'); var maxH = parseInt(maxP[0], 10), maxM = parseInt(maxP[1], 10);
            if (h > maxH || (h === maxH && m > maxM)) return false;
        }
        return true;
    }

    function buildHourOptions(hourSel, range) {
        var min = range && range.min, max = range && range.max;
        hourSel.innerHTML = '';
        for (var h = 0; h < 24; h++) {
            // Giờ h hợp lệ nếu có ít nhất 1 mốc phút (bước 5) nằm trong khoảng — tránh liệt kê giờ
            // rỗng (VD ca bắt đầu 07:30 thì 07h vẫn hợp lệ vì còn phút 30-55, nhưng 06h thì không).
            var hasValidMinute = false;
            for (var m = 0; m < 60; m += 5) { if (withinRange(h, m, min, max)) { hasValidMinute = true; break; } }
            if (hasValidMinute) hourSel.appendChild(makeHourOption(h));
        }
    }

    function buildMinuteOptions(minSel, hour, range) {
        var min = range && range.min, max = range && range.max;
        minSel.innerHTML = '';
        for (var m = 0; m < 60; m += 5) {
            if (!withinRange(hour, m, min, max)) continue;
            var o = document.createElement('option');
            var mm = String(m).padStart(2, '0');
            o.value = mm; o.text = mm;
            minSel.appendChild(o);
        }
    }

    function sync(prefix, onChange) {
        var hourSel = el(prefix, 'Hour'), minSel = el(prefix, 'Min'), hidden = el(prefix, '');
        if (!hourSel || !minSel) return;
        if (hidden) hidden.value = hourSel.value + ':' + minSel.value;
        hourSel.classList.remove('auto-time');
        minSel.classList.remove('auto-time');
        if (typeof onChange === 'function') onChange();
    }

    /**
     * @param {string} prefix
     * @param {function} [onChange] gọi mỗi khi người dùng đổi giờ/phút (sau khi đã đồng bộ input ẩn)
     */
    function init(prefix, onChange) {
        var hourSel = el(prefix, 'Hour'), minSel = el(prefix, 'Min');
        if (!hourSel || !minSel) return;

        buildHourOptions(hourSel, null);
        buildMinuteOptions(minSel, parseInt(hourSel.value, 10) || 0, null);

        hourSel.onchange = function () {
            // Đổi giờ → phút hợp lệ có thể đổi theo (VD ca giới hạn tới 16h30, chọn 16h thì phút
            // chỉ còn 00-30) — rebuild lại theo range đang áp dụng cho prefix này, giữ nguyên nếu có thể.
            var range = _ranges[prefix];
            var curM = minSel.value;
            buildMinuteOptions(minSel, parseInt(hourSel.value, 10), range);
            var stillValid = Array.prototype.some.call(minSel.options, function (o) { return o.value === curM; });
            minSel.value = stillValid ? curM : (minSel.options[0] ? minSel.options[0].value : '00');
            sync(prefix, onChange);
        };
        minSel.onchange = function () { sync(prefix, onChange); };
    }

    /**
     * Giới hạn giờ/phút chọn được theo ca làm việc (VD Gate Pass/WorkerForm chặn không cho chọn
     * ngoài giờ ca). Gọi TRƯỚC setValue() để options đã đúng phạm vi lúc set giá trị.
     * @param {string} prefix
     * @param {{min?: string, max?: string}|null} range  "HH:mm" — null/{} = bỏ giới hạn (24h đầy đủ)
     */
    function setRange(prefix, range) {
        var hourSel = el(prefix, 'Hour'), minSel = el(prefix, 'Min');
        if (!hourSel || !minSel) return;
        _ranges[prefix] = range || null;

        var curH = hourSel.value, curM = minSel.value;
        buildHourOptions(hourSel, range);
        var hourStillValid = Array.prototype.some.call(hourSel.options, function (o) { return o.value === curH; });
        hourSel.value = hourStillValid ? curH : (hourSel.options[0] ? hourSel.options[0].value : '00');

        buildMinuteOptions(minSel, parseInt(hourSel.value, 10), range);
        var minStillValid = Array.prototype.some.call(minSel.options, function (o) { return o.value === curM; });
        minSel.value = minStillValid ? curM : (minSel.options[0] ? minSel.options[0].value : '00');
    }

    /**
     * @param {string} prefix
     * @param {string} hhmm  "HH:mm", hoặc rỗng để reset về 00:00
     * @param {boolean} [isAuto] tô màu xanh nhạt (gợi ý tự động, chưa phải NV/HR tự chọn)
     * @param {boolean} [exact] true = giữ nguyên hhmm gốc khi gửi lên (KHÔNG làm tròn 5 phút) — dùng
     *   cho phía đã có giờ ERP thật/bị khóa không cho sửa, tránh lệch tới ~2.5 phút so với giờ quẹt
     *   thẻ thật khi submit (review 2026-09-22). Dropdown vẫn chỉ hiển thị gần đúng theo bước 5 phút
     *   vì phía đó luôn bị disable/ẩn, không ai thao tác — chỉ input ẩn gửi đi là cần chính xác.
     */
    function setValue(prefix, hhmm, isAuto, exact) {
        var hourSel = el(prefix, 'Hour'), minSel = el(prefix, 'Min'), hidden = el(prefix, '');
        if (!hourSel || !minSel) return;

        if (!hhmm) {
            hourSel.value = '00'; minSel.value = '00';
            if (hidden) hidden.value = '';
            hourSel.classList.remove('auto-time'); minSel.classList.remove('auto-time');
            return;
        }

        var parts = hhmm.split(':');
        var h = parseInt(parts[0], 10), m = parseInt(parts[1], 10);
        if (!Number.isFinite(h) || !Number.isFinite(m)) {
            hourSel.value = '00'; minSel.value = '00';
            if (hidden) hidden.value = '';
            hourSel.classList.remove('auto-time'); minSel.classList.remove('auto-time');
            return;
        }
        var mm = Math.round(m / 5) * 5;
        if (mm === 60) { mm = 0; h = (h + 1) % 24; }
        var hStr = String(h).padStart(2, '0'), mStr = String(mm).padStart(2, '0');

        hourSel.value = hStr; minSel.value = mStr;
        if (hidden) hidden.value = exact ? hhmm : (hStr + ':' + mStr);
        hourSel.classList.toggle('auto-time', !!isAuto);
        minSel.classList.toggle('auto-time', !!isAuto);
    }

    function getValue(prefix) {
        var hidden = el(prefix, '');
        return hidden ? hidden.value : '';
    }

    window.TimePickerHelper = { init: init, setValue: setValue, getValue: getValue, setRange: setRange };

}(window, document));
