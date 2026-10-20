# Tích hợp TPM vào My Samho (công nhân sản xuất)

Tài liệu (lưu ở HR_application/TPM.DOC) cho dev/AI làm việc trên dự án TPM: phần nào do tích hợp My Samho thêm vào, vì sao, và
ranh giới code. Cập nhật file này mỗi khi thêm/đổi phần tích hợp.

## Mục tiêu
Công nhân sản xuất dùng **app My Samho** (app di động của dự án HR, DB riêng) để:
1. **Quét mã máy** (giống `/Scan/Production` trên TPM.Web): xem thông tin máy, bấm **Báo máy hư**.
2. **Lịch sử báo hư** (giống `/Broken/History`): xem các phiếu mình đã báo, bảo trì xử lý tới đâu.

Bảo trì và Lean **không** dùng My Samho, vẫn dùng TPM.Web / app Android như cũ.

## Quyết định đã chốt (2026-10-20)
- Đối tượng: mọi nhân viên My Samho **trừ Expat và HR** (HR_api là bên kiểm tra, không phải TPM).
- Giao diện: **trang HR_web** (My Samho là vỏ WebView tải HR_web, không có màn hình native; đã sửa giả định ban đầu).
- Công nhân **không có `TPM_USER`**. Điểm chung giữa hai hệ thống là **mã nhân viên (`EMP_CD`)**.
  Phiếu lưu thẳng mã, tên, bộ phận (mã+tên), line/tổ (mã+tên) của người báo, dạng text chụp tại
  thời điểm báo (DB TPM không tra được dữ liệu HR).
- Luồng gọi: `My Samho app -> HR_api (xác thực, lấy thông tin NV) -> TPM.API`. App **không** gọi
  thẳng TPM.API. TPM sau này sẽ publish ra ngoài web, nên endpoint dành cho My Samho dùng **khóa
  nội bộ riêng**, không dùng chung `X-Api-Key` của TPM.Web/Android.

## Nguyên tắc tách biệt code
Code tích hợp nằm riêng, không trộn vào code TPM sẵn có, để bảo trì độc lập:
- Controller/DTO/service của tích hợp đặt trong thư mục/namespace riêng `External`/`MySamho`
  (xem mục "Vị trí code" khi đã có), không sửa logic `BrokenController` hiện có ngoài những chỗ
  bắt buộc (hiển thị cột người báo mới).
- Phía HR_api và My Samho cũng nằm trong module/feature riêng. Không gọi chéo ngược lại.
- Thay đổi DB là migration số thứ tự riêng, chỉ **thêm** cột/ràng buộc, không phá luồng cũ.

## Trạng thái
| Bước | Nội dung | Trạng thái |
|---|---|---|
| 1 | DB: `26_BROKEN_EXTERNAL_REPORTER.sql` (cùng thư mục TPM.DOC; khi chạy xong có thể copy vào `TPM/docs/database/new_design/`) | **Đã chạy xong trên MES** — đã verify `REPORTED_BY` nullable + đủ 6 cột `REPORTED_*` mới |
| 2 | TPM.API: endpoint nhóm My Samho + hiển thị người báo | Đã code + build + test (xem bên dưới), **chưa deploy**, cần cấu hình khóa |
| 3 | HR_api: endpoint cầu nối, chặn Expat/HR | Đã code + build + test nối thật với TPM.API (chỉ đọc). Cần cấu hình `Tpm:BaseUrl`, `Tpm:InternalKey` |
| 4 | HR_web: 2 trang cho công nhân (My Samho là vỏ WebView tải HR_web) | Đã code + build, **chưa test trên thiết bị/chưa deploy**;  |
| — | Migration 27 (cấp Tổ) | Đã chạy trên MES, TPM.API/TPM.Web đã publish bản Release (khóa đã đặt) |

## TPM.API (bước 2)
Code nằm riêng trong `TPM.API/MySamho/` (3 file): `MySamhoKeyAttribute.cs` (kiểm khóa),
`MySamhoModels.cs` (DTO + mã lỗi riêng), `MySamhoController.cs` (route `api/mysamho`).

**Xác thực:** header `X-MySamho-Key` phải khớp cấu hình `MySamho:InternalKey`. Chưa cấu hình =
trả 503 (chặn hết). Khóa này chỉ HR_api giữ, tách khỏi `Security:ApiKeys`. Việc cần làm khi deploy:
đặt `MySamho:InternalKey` trong `appsettings.Production.json` của TPM.API (chuỗi ngẫu nhiên dài),
dùng cùng giá trị ở HR_api. Local dev: User Secrets hoặc biến môi trường `MySamho__InternalKey`.

**Endpoint** (JSON trả về giữ PascalCase như phần còn lại của TPM.API; lỗi có `success`, `code`, `message`):
| Method | Route | Mục đích |
|---|---|---|
| GET | `api/mysamho/machines/{itemCode}` | Thông tin máy (không có giá mua/khấu hao/ghi chú) + `OpenBroken` nếu máy đang có phiếu mở. 404 `MACHINE_NOT_FOUND` |
| POST | `api/mysamho/broken/report` | Body: `ItemCode, Description (<=500), EmpCd, Name, DeptCd, DeptNm, LineCd, LineNm` (4 trường cuối tùy chọn). Trả `brokenId` + `suggestedAreaUserNms`. Lỗi: 400 `MYSAMHO_MISSING_REPORTER` / `MISSING_ITEM_CODE` / `MISSING_DESCRIPTION` / `MYSAMHO_DESCRIPTION_TOO_LONG`, 404 `MACHINE_NOT_FOUND`, 409 `BROKEN_ALREADY_OPEN` |
| GET | `api/mysamho/broken?empCd=...&status=...` | Phiếu của chính mã NV đó, mới nhất trước, tối đa 200 dòng |

Báo hư từ My Samho giống luồng TPM.Web: tạo phiếu `REPORTED`, đổi máy sang `REPAIR`, ghi nhật ký
`BROKEN_REPORT`; khác ở chỗ `REPORTED_BY`, `TPM_MACHINE.UPDATED_BY`, `TPM_MACHINE_HISTORY.CREATED_BY`
để nguyên NULL và người báo ghi ở các cột `*_EMP_CD/NAME`.

**Hai chỗ sửa code TPM sẵn có (bắt buộc):**
- `Infrastructure/ApiKeyMiddleware.cs`: thêm `/api/mysamho` vào danh sách bỏ qua (route này có khóa riêng).
- `Controllers/BrokenController.cs`: `JOIN TPM_USER rep` đổi thành `LEFT JOIN`, nếu không phiếu của
  công nhân (REPORTED_BY NULL) sẽ **biến mất** khỏi danh sách trên TPM.Web. Cột người báo hiển thị
  `Tên (mã NV) · Line` cho phiếu My Samho; phiếu TPM.Web giữ nguyên.

**Đã test** (local, DB MES thật): 401 khi thiếu/sai khóa, 400 khi thiếu người báo, tra cứu máy đúng,
danh sách phiếu chạy được, endpoint JWT cũ vẫn trả 401 khi không token. **Chưa test thực tế**
`broken/report` (ghi vào DB dùng chung dev/prod) — nên test một lần có chủ đích bằng máy thử.

## HR_api (bước 3)
Module tự chứa `HR_api/Tpm/` (`TpmController.cs`, `TpmService.cs`, `TpmModels.cs`), đăng ký 1 dòng ở
`Program.cs`, cấu hình ở `appsettings.json` mục `Tpm` (`BaseUrl` = địa chỉ TPM.API, `InternalKey` = cùng
giá trị `MySamho:InternalKey` bên TPM.API; để trống = trả `TPM_UNAVAILABLE`).

| Route (qua `X-Api-Key` của HR_api như mọi route khác) | Việc |
|---|---|
| `GET apiHR/Tpm/machine?empcd=&itemCode=` | Chuyển tới `api/mysamho/machines/{itemCode}` |
| `POST apiHR/Tpm/report` body `{empcd, itemCode, description}` | Tra tên/dept/line/tổ của NV rồi chuyển tới `api/mysamho/broken/report` |
| `GET apiHR/Tpm/history?empcd=&status=` | Chuyển tới `api/mysamho/broken?empCd=` (chỉ phiếu của NV đó) |

- **Tin ai:** app chỉ gửi mã NV (đúng mô hình HR_api hiện có, không có JWT). Tên, bộ phận, line, tổ
  HR_api tự tra: `HRMS.ECM100` (CNAME, DEPTCD/LINECD/WORKCD, JEAJIKGB='Y' = còn đi làm) JOIN
  `HRMS.EAM410` (DEPTNM, TEAMNM = tên Line, WORKNM = tên Tổ). Không tin dữ liệu app gửi.
- **Chặn:** mã không tồn tại/đã nghỉ/`IS_ACTIVE=0`, và `ROLE_ID` 5 (HR), 7 (Expat).
- **Phản hồi:** giữ nguyên JSON của TPM (PascalCase, có `success/code/message`); lỗi nghiệp vụ của HR_api
  trả 200 + `success=false` như quy ước HR_api. TPM không kết nối được -> `code=TPM_UNAVAILABLE`.
- Lưu ý chung của HR_api: ai có `X-Api-Key` đều giả được mã NV khác (áp dụng cho mọi route HR_api, không
  riêng module này).

## HR_web (bước 4) — giao diện công nhân
Module tự chứa: `HR_web/API/Service/TpmService.cs` (gọi `apiHR/Tpm/*`, trả raw JSON),
`HR_web/Controllers/Tpm/TpmController.cs`, `HR_web/Views/Tpm/Scan.cshtml` + `History.cshtml`. Thay đổi
ngoài module: 1 dòng đăng ký ở `Program.cs`, 2 mục menu trong nhóm "Cá nhân" ở
`Helpers/SideMenuBuilder.cs` (ẩn với HR; Expat vốn không thấy nhóm này).

- **/Tpm/Scan**: quét mã vạch tem máy (CODE128; chấp nhận cả CODE39/QR) -> hiện thông tin máy -> mô tả
  lỗi -> Báo máy hư. Nếu máy đã có phiếu mở thì hiện trạng thái, không cho báo lại.
- **/Tpm/History**: các phiếu của chính mình (lọc Tất cả / Đang xử lý / Đã xong).
- **Cách quét (chốt 2026-10-20):** (1) **Chụp ảnh tem để quét**: mở camera chụp ảnh của app (hộp chọn
  file có sẵn trong vỏ app, như chụp ảnh gửi HR) rồi đọc mã vạch từ ảnh bằng `html5-qrcode.scanFile`;
  (2) ô **gõ tay** mã máy. Không có "quét trực tiếp" vì `getUserMedia` chỉ chạy trên HTTPS (mạng nội bộ
  `sa1-hanaro.esamho.com` là HTTP) và Android phải phát hành bản mới. **Không cần cập nhật app Android/iOS**,
  chỉ deploy HR_web + HR_api. Đọc mã vạch 1D (CODE128) từ ảnh kém ổn định hơn quét trực tiếp — test với tem
  thật; nếu hụt nhiều thì quay lại phương án quét trực tiếp (xem mục dưới).
- EMPCD luôn lấy từ session, HR_web không tin client. HR và Expat bị chặn ở HR_web và HR_api.
- **Quét gốc trong app (chốt 2026-10-20, theo ý sếp: nhanh hơn quét bằng thẻ HTML):** vỏ app có thêm lệnh
  quét mã vạch gốc, trang web tự dùng khi app hỗ trợ.
  - Web -> app: Android `window.Android.scanBarcode()` (`addJavascriptInterface` sẵn có), iOS
    `window.webkit.messageHandlers.scanBarcode.postMessage(null)`.
  - App -> web: `window.onNativeBarcode(code)` / `onNativeBarcodeCancel()` / `onNativeBarcodeError(msg)`
    (`msg='camera_denied'` khi chưa cấp quyền camera).
  - Android (`MainActivity.java`, `startNativeBarcodeScan`): **Google Code Scanner** (ML Kit,
    `play-services-code-scanner:16.1.0`), chỉ CODE128, auto-zoom; manifest có meta-data tải sẵn module
    `barcode_ui`. Máy không có Google Play Services -> báo lỗi -> trang tự rơi về quét trong trang.
  - iOS (`BarcodeScannerViewController.swift` mới + `ViewController.swift`): **AVFoundation**
    (`AVCaptureMetadataOutput`, `.code128`), Full HD, lấy nét liên tục + vùng gần, chỉ giải mã trong khung
    hướng dẫn, rung khi quét được. `Info.plist` cập nhật mô tả quyền camera (nêu quét tem máy).
  - **App cũ (chưa cập nhật)** không có bridge nên trang vẫn quét bằng html5-qrcode trong trang (cần HTTPS) và
    gõ tay như trước — không hỏng.
  - **Phải phát hành bản mới** cho cả Android và iOS thì mới có quét gốc. Cả hai đã biên dịch thành công,
    **chưa chạy thử trên thiết bị thật**.

## Hiển thị cho bảo trì (TPM.Web)
Trang `Broken/Index` hiện: máy (mã + tên), vị trí máy, người báo `Tên (mã NV)`, và dòng nhỏ
`Bộ phận · Line · Tổ` của công nhân (chỉ có ở phiếu từ My Samho), kèm giờ báo. Dữ liệu từ
`REPORTED_DEPT_NM/LINE_NM/WORK_NM` (migration 26 + 27).

## DB (migration 26, 27)
Migration 27 (`27_BROKEN_REPORTER_WORK.sql`) thêm `REPORTED_WORK_CD/NM`. **Phải chạy trước khi deploy
TPM.API/TPM.Web bản có MySamho**, vì `BrokenController` (danh sách của TPM.Web) và báo hư My Samho đều
đọc/ghi cột này, chưa chạy 27 thì danh sách báo hư trên TPM.Web sẽ lỗi ORA-00904.


Bảng `TPM_MACHINE_BROKEN`:
- `REPORTED_BY` giờ **nullable**. Phiếu từ TPM.Web vẫn điền `REPORTED_BY`; phiếu từ My Samho để
  `NULL`.
- Thêm `REPORTED_EMP_CD`, `REPORTED_NAME`, `REPORTED_DEPT_CD/NM`, `REPORTED_LINE_CD/NM`.
- Ràng buộc `TPM_MACHINE_BROKEN_REPORTER_CK`: phải có `REPORTED_BY` hoặc `REPORTED_EMP_CD`.
- Index `TPM_MACHINE_BROKEN_EMP_N1` cho lọc "phiếu của tôi" theo mã nhân viên.

Bảng `TPM_MACHINE_HISTORY`: thêm `CREATED_EMP_CD`, `CREATED_NAME` cho dòng `BROKEN_REPORT` từ
hệ thống ngoài (`CREATED_BY` để NULL).

Lưu ý: `00_SCHEMA_FULL.sql` hiện chưa có `TPM_MACHINE_BROKEN`, nên migration 26 chưa được gộp vào
baseline. Khi gộp baseline thì gộp luôn các cột này.

## Điều cần nhớ khi code tiếp
- `BrokenController.Report` hiện ghi `REPORTED_BY`, `TPM_MACHINE.UPDATED_BY` và
  `TPM_MACHINE_HISTORY.CREATED_BY` đều bằng `USER_ID`. Với người báo từ My Samho các cột này để
  NULL (hoặc không cập nhật `UPDATED_BY`), thông tin người báo ghi vào cột `*_EMP_CD/NAME` mới.
- "Phiếu của tôi" của My Samho lọc theo `REPORTED_EMP_CD`, **không** theo `REPORTED_BY`.
- Máy chỉ được có 1 phiếu mở (unique index `TPM_MACHINE_BROKEN_OPEN_UX`), giữ nguyên.
- API tra cứu máy cho công nhân chỉ trả thông tin cần xem, không trả giá mua/khấu hao/số dư.
- `docs/CONTEXT.md` mô tả role PROD_STAFF dùng Web; hướng My Samho này không dùng role đó.
