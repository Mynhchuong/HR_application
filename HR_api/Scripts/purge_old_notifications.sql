-- ============================================================
-- Tự động xoá thông báo cũ (> 40 ngày) để giảm tải bảng
-- HR_NOTIFICATIONS / HR_NOTIFICATION_LOG / HR_NOTIFICATION_TARGET
-- (bảng càng phình to càng làm chậm query unread-count chạy liên tục,
-- xem index_notification_perf.sql cho vấn đề liên quan).
--
-- Dùng DBMS_SCHEDULER (không dùng trigger vì trigger chỉ chạy khi có
-- INSERT/UPDATE/DELETE, không tự chạy theo thời gian).
-- Compatible: Oracle 10g+ (DBMS_SCHEDULER có từ 10g).
--
-- Xoá theo CREATED_DATE của HR_NOTIFICATIONS. Xoá bảng con trước
-- (HR_NOTIFICATION_LOG, HR_NOTIFICATION_TARGET) rồi mới xoá bảng cha,
-- để an toàn dù có FK constraint hay không.
-- ============================================================

CREATE OR REPLACE PROCEDURE HRMS.SP_PURGE_OLD_NOTIFICATIONS (
    p_days_old IN NUMBER DEFAULT 40
)
AS
    v_deleted_count NUMBER := 0;
BEGIN
    -- Xoá log đọc/chưa đọc của các thông báo cũ
    DELETE FROM HRMS.HR_NOTIFICATION_LOG
     WHERE NOTI_ID IN (
        SELECT ID FROM HRMS.HR_NOTIFICATIONS
         WHERE CREATED_DATE < SYSDATE - p_days_old
     );

    -- Xoá target (dùng cho loại MULTI) của các thông báo cũ
    DELETE FROM HRMS.HR_NOTIFICATION_TARGET
     WHERE NOTI_ID IN (
        SELECT ID FROM HRMS.HR_NOTIFICATIONS
         WHERE CREATED_DATE < SYSDATE - p_days_old
     );

    -- Xoá chính thông báo cũ
    DELETE FROM HRMS.HR_NOTIFICATIONS
     WHERE CREATED_DATE < SYSDATE - p_days_old;

    v_deleted_count := SQL%ROWCOUNT;

    COMMIT;
EXCEPTION
    WHEN OTHERS THEN
        ROLLBACK;
        RAISE;
END SP_PURGE_OLD_NOTIFICATIONS;
/

-- ============================================================
-- Job tự chạy hằng ngày lúc 2h sáng (off-peak)
-- ============================================================
BEGIN
    DBMS_SCHEDULER.CREATE_JOB (
        job_name        => 'HRMS.JOB_PURGE_OLD_NOTIFICATIONS',
        job_type        => 'STORED_PROCEDURE',
        job_action      => 'HRMS.SP_PURGE_OLD_NOTIFICATIONS',
        start_date      => TRUNC(SYSDATE) + 1 + (2/24),  -- 2h sáng ngày mai
        repeat_interval => 'FREQ=DAILY; BYHOUR=2; BYMINUTE=0',
        enabled         => TRUE,
        comments        => 'Xoa thong bao cu hon 40 ngay, chay hang dem luc 2h sang'
    );
END;
/

-- ============================================================
-- Kiểm tra job đã tạo:
--   SELECT JOB_NAME, ENABLED, STATE, NEXT_RUN_DATE FROM USER_SCHEDULER_JOBS
--    WHERE JOB_NAME = 'JOB_PURGE_OLD_NOTIFICATIONS';
--
-- Chạy thử ngay (không cần chờ tới 2h sáng):
--   EXEC HRMS.SP_PURGE_OLD_NOTIFICATIONS;
--
-- Tắt job nếu cần:
--   EXEC DBMS_SCHEDULER.DISABLE('HRMS.JOB_PURGE_OLD_NOTIFICATIONS');
--
-- LƯU Ý: lần chạy đầu tiên có thể xoá nhiều dữ liệu tồn đọng cùng lúc
-- (toàn bộ thông báo cũ hơn 40 ngày từ trước tới giờ) — nên chạy thử
-- ngoài giờ cao điểm lần đầu.
-- ============================================================
