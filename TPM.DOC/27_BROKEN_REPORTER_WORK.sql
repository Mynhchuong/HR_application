-- Bổ sung cấp "Tổ" (WORK) cho thông tin người báo từ My Samho. HR có 3 cấp tổ chức
-- Bộ phận (DEPT) -> Line (LINE) -> Tổ (WORK); migration 26 mới chỉ có DEPT và LINE.
-- Xem README.md cùng thư mục (HR_application/TPM.DOC).
-- CHẠY TRƯỚC KHI deploy TPM.API bản có MySamho (code insert cột này).

ALTER TABLE TPM_MACHINE_BROKEN ADD
(
  REPORTED_WORK_CD  VARCHAR2(30),
  REPORTED_WORK_NM  VARCHAR2(150)
);
