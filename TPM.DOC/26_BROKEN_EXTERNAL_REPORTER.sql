-- Cho phép công nhân sản xuất báo máy hư từ hệ thống ngoài (app My Samho) mà KHÔNG cần tài khoản
-- TPM_USER. DB TPM khác DB My Samho, điểm chung là mã nhân viên (EMP_CD) nên phiếu lưu thẳng
-- thông tin người báo (mã/tên/bộ phận/line-tổ) dạng text chụp tại thời điểm báo.
-- Xem README.md cùng thư mục (HR_application/TPM.DOC).
--
-- Phiếu do bảo trì/Lean báo trên TPM.Web vẫn dùng REPORTED_BY như cũ; phiếu từ My Samho để
-- REPORTED_BY = NULL và điền các cột REPORTED_* bên dưới.

ALTER TABLE TPM_MACHINE_BROKEN MODIFY (REPORTED_BY NULL);

ALTER TABLE TPM_MACHINE_BROKEN ADD
(
  REPORTED_EMP_CD   VARCHAR2(20),
  REPORTED_NAME     VARCHAR2(150),
  REPORTED_DEPT_CD  VARCHAR2(30),
  REPORTED_DEPT_NM  VARCHAR2(150),
  REPORTED_LINE_CD  VARCHAR2(30),
  REPORTED_LINE_NM  VARCHAR2(150)
);

-- Phải có người báo: hoặc là user TPM, hoặc là mã nhân viên từ hệ thống ngoài.
ALTER TABLE TPM_MACHINE_BROKEN ADD CONSTRAINT TPM_MACHINE_BROKEN_REPORTER_CK
  CHECK (REPORTED_BY IS NOT NULL OR REPORTED_EMP_CD IS NOT NULL);

-- Trang "Lịch sử báo hư" của công nhân lọc theo mã nhân viên.
CREATE INDEX TPM_MACHINE_BROKEN_EMP_N1 ON TPM_MACHINE_BROKEN (REPORTED_EMP_CD);

-- Nhật ký máy: dòng BROKEN_REPORT từ hệ thống ngoài không có CREATED_BY (cột này đã nullable),
-- ghi người báo bằng mã + tên thay thế.
ALTER TABLE TPM_MACHINE_HISTORY ADD
(
  CREATED_EMP_CD  VARCHAR2(20),
  CREATED_NAME    VARCHAR2(150)
);
