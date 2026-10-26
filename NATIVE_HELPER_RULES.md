# Quy tắc viết code native (iOS/Android) cho app My Samho

App My Samho về bản chất là **WebView bọc HR_web**. Mọi lần sửa code native (Swift/Java) đều phải
qua 1 vòng build → App Store/Play Store → NV tự cập nhật — **chậm và rủi ro** (hiện iOS còn đang bị
Apple chặn phân phối ở Việt Nam, xem `project_ios_app_unavailable_vn` trong bộ nhớ). Vì vậy:

> **Code web sửa xong là NV dùng được ngay. Code native sửa xong phải chờ NV tự cập nhật app — có thể
> rất lâu, hoặc không bao giờ với 1 số máy.** Né code native hết mức có thể, và khi bắt buộc phải thêm,
> viết sao cho **không bao giờ phải sửa lại** khi có tính năng mới tương tự.

## 1. Mỗi bridge native phải là HELPER DÙNG CHUNG, không phải tính năng riêng lẻ

❌ Sai: viết bridge `tpmScanBarcode` riêng cho tính năng TPM, rồi khi Directory cần quét QR lại viết
thêm `directoryScanBarcode` riêng.

✅ Đúng: viết đúng 1 bridge `scanBarcode` generic (không biết/không quan tâm ai gọi, làm gì với kết
quả) — mọi trang web cần quét camera đều gọi lại đúng bridge này. Đã làm đúng kiểu này với:
- `scanBarcode` — TPM dùng, Directory tra cứu NV cũng dùng lại y hệt, không sửa 1 dòng native nào.
- `saveCredentials`/`clearCredentials` — chỉ 2 bridge, dùng được cho toàn bộ luồng đăng nhập Face
  ID/vân tay, không gắn với 1 trang cụ thể nào.
- `setEmpcd` — generic "báo mã NV hiện tại cho native", không phải riêng cho tính năng ép cập nhật.

**Khi thêm bridge mới, luôn tự hỏi: "cái NÀY có phải chỉ con đường DỮ LIỆU (chuỗi, số, callback) hay
đang lẫn cả LOGIC NGHIỆP VỤ vào native?"** Nếu có logic nghiệp vụ (vd "chỉ quét được khi đang ở màn
hình TPM") → sai, phải đẩy hết nghiệp vụ đó về phía web, native chỉ làm đúng 1 việc kỹ thuật thuần tuý.

## 2. Luôn kiểm tra TRƯỚC: có cần bridge mới thật không, hay OS đã tự làm sẵn?

Trước khi viết bridge mới, kiểm tra app đã tự động xử lý việc đó chưa — nhiều việc tưởng cần native
nhưng thật ra WebView đã tự lo:
- **Tải file/ảnh**: không cần bridge riêng — chỉ cần dùng `<a download>` bình thường, iOS
  (`WKDownloadDelegate`) và Android (`DownloadListener`+`DownloadManager`) đã tự bắt và xử lý đẹp
  (share sheet lưu Photos / DownloadManager vào Downloads). Xem ví dụ `MediaViewer` (mục "Lưu ảnh").
- **Chọn file/chụp ảnh** (`<input type="file" capture>`): Android đã có `onShowFileChooser` xử lý đủ
  camera/thư viện; iOS WKWebView bản mới cũng tự xử lý.
- **Alert/confirm JS** (`alert()`, `confirm()`): WKUIDelegate/WebChromeClient đã tự hiện dialog native.

Chỉ viết bridge mới khi thật sự KHÔNG có cách nào làm qua Web API chuẩn (camera realtime để quét mã,
sinh trắc học, lưu credential bảo mật phần cứng, push notification, đọc version app).

## 3. Mọi bridge PHẢI có fallback web thuần — không phụ thuộc app

Link chạy ở 3 môi trường: **desktop browser, app (WebView), trình duyệt mobile**. Web luôn là bản
chạy được ở cả 3 nơi; native chỉ là NÂNG CẤP khi đang chạy trong app, không bao giờ là điều kiện bắt
buộc.

```js
if (window.webkit?.messageHandlers?.xxx) {
    // có app iOS -> dùng native
} else if (window.Android?.xxx) {
    // có app Android -> dùng native
} else {
    // KHÔNG có app -> fallback web thuần, PHẢI chạy đúng y hệt về mặt chức năng
}
```

Xem `native-scanner.js` (quét mã) và `Tpm/Scan.cshtml` (`startLive()` dùng `html5-qrcode` làm fallback
JS) làm mẫu.

## 4. Gom logic web dùng chung thành 1 file helper JS, không copy giữa các trang

Mỗi bridge/luồng native nên có đúng 1 file JS helper dùng chung (`wwwroot/js/*.js`), các trang chỉ
include + gọi — KHÔNG copy nguyên đoạn code bridge vào từng `.cshtml`. Mẫu đã làm:
- `native-scanner.js` — `NativeScanner.scan({onResult, onCancel, onError})`.
- `media-viewer.js` — `MediaViewer.open(urls, index)`.

## 5. Khi bắt buộc đổi logic, ưu tiên đẩy xuống server config thay vì native

Nếu hành vi có thể thay đổi sau này (vd danh sách symbology quét mã, ngưỡng version ép cập nhật) —
đừng hard-code trong Swift/Java. Đẩy xuống `appsettings.json`/DB, native chỉ đọc lại qua API — sửa
được mà không cần build lại app. Mẫu: `AppVersionController.Check` đọc `MinVersion` từ
`appsettings.json`, đổi 1 dòng config là có hiệu lực ngay cho mọi máy, không cần submit bản mới.

## Checklist trước khi thêm code native mới

1. Đã kiểm tra OS/WebView có tự làm sẵn việc này chưa (mục 2)?
2. Bridge đặt tên GENERIC, không gắn tên tính năng cụ thể nào?
3. Có viết fallback web thuần chạy được khi không có app không?
4. Logic nghiệp vụ nằm ở web/server, native chỉ truyền dữ liệu thuần?
5. Có chỗ nào nên đẩy xuống config server thay vì hard-code trong app không?

Nếu cả 5 câu trên đều ổn — mới viết code native, và chỉ viết 1 lần, không phải sửa lại khi có tính
năng tương tự sau này.
