-- ════════════════════════════════════════════════════════════════════════════
-- CHIA SẺ BẢN TIN QUA EMAIL + FACEBOOK (2026-11-20)
-- Mỗi bản tin × mỗi kênh (EMAIL / FB) có 1 dòng: lưu nháp, trạng thái, lần gửi gần nhất.
-- Nội dung gửi = TOÀN BỘ bài (tiếng Việt + "English below:" + tiếng Anh) lấy từ HR_BULLETIN lúc
-- gửi; bảng này chỉ lưu phần riêng của từng kênh (tiêu đề mail, người nhận, lời dẫn, nội dung FB đã sửa).
-- HR_BULLETIN_SHARE_CFG: cấu hình Facebook (Page ID + Page Access Token) — HR/Admin dán ở trang
-- BulletinAdmin/FacebookSettings, không để trong appsettings để đổi token không cần deploy.
-- ════════════════════════════════════════════════════════════════════════════

CREATE TABLE HRMS.HR_BULLETIN_SHARE (
    ID              NUMBER          NOT NULL,
    BULLETIN_ID     NUMBER          NOT NULL,
    CHANNEL         VARCHAR2(10)    NOT NULL,            -- EMAIL | FB
    STATUS          VARCHAR2(10)    DEFAULT 'DRAFT' NOT NULL,   -- DRAFT | SENT | FAILED
    SUBJECT         NVARCHAR2(500),                      -- EMAIL: tiêu đề mail
    RECIPIENTS      VARCHAR2(2000),                      -- EMAIL: danh sách mail, cách nhau dấu phẩy
    INTRO_VI        NVARCHAR2(1000),                     -- EMAIL: lời dẫn đầu mail (tiếng Việt)
    INTRO_EN        NVARCHAR2(1000),                     -- EMAIL: lời dẫn (tiếng Anh)
    FB_MESSAGE      NCLOB,                               -- FB: nội dung bài đăng (chữ thường, đã sửa)
    FB_WITH_IMAGE   NUMBER(1)       DEFAULT 1 NOT NULL,  -- FB: đăng kèm ảnh bìa
    FB_POST_ID      VARCHAR2(100),                       -- FB: id bài trên Fanpage sau khi đăng
    LAST_ERROR      NVARCHAR2(1000),
    SENT_DT         DATE,
    SENT_BY         VARCHAR2(50),
    INST_ID         VARCHAR2(50),
    INST_DT         DATE            DEFAULT SYSDATE,
    UPDT_ID         VARCHAR2(50),
    UPDT_DT         DATE,
    CONSTRAINT PK_HR_BULLETIN_SHARE PRIMARY KEY (ID),
    CONSTRAINT UQ_HR_BULLETIN_SHARE UNIQUE (BULLETIN_ID, CHANNEL),
    CONSTRAINT CHK_BLT_SHARE_CH CHECK (CHANNEL IN ('EMAIL', 'FB')),
    CONSTRAINT CHK_BLT_SHARE_ST CHECK (STATUS IN ('DRAFT', 'SENT', 'FAILED'))
) TABLESPACE HRMSD
LOB (FB_MESSAGE) STORE AS (
    TABLESPACE HRMSD ENABLE STORAGE IN ROW CHUNK 8192 PCTVERSION 10
    NOCACHE LOGGING
);

CREATE SEQUENCE HRMS.SEQ_HR_BULLETIN_SHARE START WITH 1 INCREMENT BY 1 NOCACHE;

CREATE TABLE HRMS.HR_BULLETIN_SHARE_CFG (
    CFG_KEY         VARCHAR2(50)    NOT NULL,            -- FB_PAGE_ID | FB_PAGE_TOKEN | FB_PAGE_NAME
    CFG_VALUE       VARCHAR2(1000),
    UPDT_ID         VARCHAR2(50),
    UPDT_DT         DATE            DEFAULT SYSDATE,
    CONSTRAINT PK_HR_BULLETIN_SHARE_CFG PRIMARY KEY (CFG_KEY)
) TABLESPACE HRMSD;
