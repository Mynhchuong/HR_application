// Helper dùng chung cho MỌI trang cần quét barcode/QR bằng camera gốc của app (Android: Google Code
// Scanner — ML Kit, iOS: VisionKit DataScannerViewController) — nhanh, tự lấy nét/zoom, nhạy hơn hẳn
// quét bằng JS (html5-qrcode) trong WebView, không cần HTTPS.
//
// Bridge native (scanBarcode) đã có sẵn và GENERIC từ trước (TPM dùng, giờ Directory tra cứu NV cũng
// dùng chung) — side native KHÔNG cần sửa gì thêm khi có trang mới muốn quét, chỉ cần include file
// này rồi gọi window.NativeScanner.scan({...}) (yêu cầu 2026-10-26: "mốt call lấy ra sẵn, mốt code
// không cần cập nhật iOS Android nữa").
//
// Cách dùng:
//   <script src="~/js/native-scanner.js"></script>
//   ...
//   function startScan() {
//       var started = window.NativeScanner.scan({
//           onResult:  function (code) { ... },   // quét thành công, code = chuỗi giải mã được
//           onCancel:  function () { ... },        // user bấm Đóng, không quét gì
//           onError:   function (msg) { ... }      // 'camera_denied' hoặc lỗi khác (máy không hỗ trợ)
//       });
//       if (!started) { /* không có native -> tự quét bằng JS (html5-qrcode) làm fallback */ }
//   }
(function () {
    function isAvailable() {
        return (window.Android && typeof window.Android.scanBarcode === 'function') ||
            !!(window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.scanBarcode);
    }

    // Máy báo lỗi 1 lần (vd Android không có Google Play Services) -> coi như hỏng cho hết phiên trang
    // này, không thử native lại nữa (tránh user bấm lại vẫn dính lỗi y hệt) — mỗi trang tự fallback JS.
    var broken = false;
    var handlers = { onResult: null, onCancel: null, onError: null };

    function scan(opts) {
        opts = opts || {};
        if (!isAvailable() || broken) return false;

        handlers.onResult = typeof opts.onResult === 'function' ? opts.onResult : function () {};
        handlers.onCancel = typeof opts.onCancel === 'function' ? opts.onCancel : function () {};
        handlers.onError  = typeof opts.onError  === 'function' ? opts.onError  : function () {};

        if (window.Android && typeof window.Android.scanBarcode === 'function') window.Android.scanBarcode();
        else window.webkit.messageHandlers.scanBarcode.postMessage(null);
        return true;
    }

    // Callback native gọi ngược vào — tên hàm cố định theo bridge iOS/Android đã có sẵn (ViewController.swift
    // callWeb(...) / MainActivity.callWeb(...)), KHÔNG đổi tên nếu không sửa cả 2 app native.
    window.onNativeBarcode = function (code) { handlers.onResult(code); };
    window.onNativeBarcodeCancel = function () { handlers.onCancel(); };
    window.onNativeBarcodeError = function (msg) {
        if (msg !== 'camera_denied') broken = true;
        handlers.onError(msg);
    };

    window.NativeScanner = { isAvailable: isAvailable, scan: scan };
})();
