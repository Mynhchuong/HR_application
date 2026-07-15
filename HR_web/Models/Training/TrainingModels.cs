using System;
using System.Collections.Generic;

namespace HR_web.Models.Training;

public class SelfRegisterRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
}

public class CheckInRequest
{
    public int SESSION_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public double? LATITUDE { get; set; }
    public double? LONGITUDE { get; set; }
}

public class MaterialViewRequest
{
    public int MATERIAL_ID { get; set; }
    public string EMPCD { get; set; } = "";
}

public class AskQuestionRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string QUESTION_TEXT { get; set; } = "";
}

public class StartAttemptRequest
{
    public int TEST_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string? IP_ADDRESS { get; set; }
    public string? USER_AGENT { get; set; }
}

public class SaveAnswerRequest
{
    public int ATTEMPT_ID { get; set; }
    public int QUESTION_ID { get; set; }
    public string? ANSWER_OPTION_IDS { get; set; }
    public string? ANSWER_TEXT { get; set; }
    public string EMPCD { get; set; } = "";
}

public class SubmitAttemptRequest
{
    public int ATTEMPT_ID { get; set; }
    public string EMPCD { get; set; } = "";
}

public class SubmitReviewRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public decimal RATING_CONTENT { get; set; }
    public decimal RATING_TEACHER { get; set; }
    public string? COMMENT_TEXT { get; set; }
}

public class ConfirmAttendanceRequest
{
    public int SESSION_ID { get; set; }
    public string ATTENDEE_EMPCD { get; set; } = "";
    public string ATTENDANCE_STATUS { get; set; } = "";
    public string? REASON { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class ConfirmAttendanceBatchRequest
{
    public int SESSION_ID { get; set; }
    public string? GROUP_NAME { get; set; }
    public string ATTENDANCE_STATUS { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class DropStudentRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class SaveMaterialRequest
{
    public int? ID { get; set; }
    public string MATERIAL_LEVEL { get; set; } = "CLASS";
    public int? COURSE_ID { get; set; }
    public int? CLASS_ID { get; set; }
    public string TITLE { get; set; } = "";
    public string FILE_NAME { get; set; } = "";
    public string FILE_TYPE { get; set; } = "";
    public string? FILE_URL { get; set; }
    public int IS_REQUIRED { get; set; }
    public int DISPLAY_ORDER { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class DeleteMaterialRequest
{
    public int ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class AnswerQuestionRequest
{
    public int ID { get; set; }
    public string ANSWER_TEXT { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class DeleteQuestionRequest
{
    public int ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class SaveTestRequest
{
    public int? ID { get; set; }
    public int? CLASS_ID { get; set; }
    public int IS_TEMPLATE { get; set; }
    public string TITLE { get; set; } = "";
    public string? DESCRIPTION { get; set; }
    public int DURATION_MINUTES { get; set; }
    public DateTime? AVAILABLE_FROM { get; set; }
    public DateTime? AVAILABLE_TO { get; set; }
    public decimal PASS_SCORE { get; set; }
    public int MAX_ATTEMPTS { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class TestQuestionInputModel
{
    public int? ID { get; set; }
    public string QUESTION_TYPE { get; set; } = "SINGLE";
    public string QUESTION_TEXT { get; set; } = "";
    public decimal POINTS { get; set; }
    public int DISPLAY_ORDER { get; set; }
    public List<TestOptionInputModel> OPTIONS { get; set; } = new();
}

public class TestOptionInputModel
{
    public int? ID { get; set; }
    public string OPTION_TEXT { get; set; } = "";
    public int IS_CORRECT { get; set; }
    public int DISPLAY_ORDER { get; set; }
}

public class SaveTestQuestionsRequest
{
    public int TEST_ID { get; set; }
    public List<TestQuestionInputModel> QUESTIONS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}

public class ChangeTestStatusRequest
{
    public int ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
    public DateTime? AVAILABLE_FROM { get; set; }
    public DateTime? AVAILABLE_TO { get; set; }
}

public class GradeAnswerRequest
{
    public int ANSWER_ID { get; set; }
    public decimal POINTS_AWARDED { get; set; }
    public string? GRADER_COMMENT { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class SaveCourseRequest
{
    public int? ID { get; set; }
    public string TITLE { get; set; } = "";
    public string? DESCRIPTION { get; set; }
    public string? OBJECTIVES { get; set; }
    public string? CATEGORY { get; set; }
    public string COURSE_MODE { get; set; } = "STANDARD";
    public int? DEFAULT_DURATION_MIN { get; set; }
    public int AUTO_OPEN_MONTHLY { get; set; }
    public int? AUTO_OPEN_DAY { get; set; }
    public decimal? DEFAULT_PASS_SCORE { get; set; }
    public decimal? DEFAULT_MIN_ATTEND_PCT { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class SaveClassRequest
{
    public int? ID { get; set; }
    public int COURSE_ID { get; set; }
    public string CLASS_NAME { get; set; } = "";
    public string? DESCRIPTION { get; set; }
    public string REGISTRATION_MODE { get; set; } = "ASSIGNED";
    public int? MAX_STUDENTS { get; set; }
    public DateTime? REGISTRATION_DEADLINE { get; set; }
    public DateTime? START_DATE { get; set; }
    public DateTime? END_DATE { get; set; }
    public decimal? MIN_ATTENDANCE_PERCENT { get; set; }
    public int? FINAL_TEST_ID { get; set; }
    public int? REQUIRE_POST_REVIEW { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class SaveSessionRequest
{
    public int? ID { get; set; }
    public int CLASS_ID { get; set; }
    public int SESSION_NO { get; set; }
    public DateTime SESSION_DATE { get; set; }
    public string START_TIME { get; set; } = "";
    public string END_TIME { get; set; } = "";
    public string? TOPIC { get; set; }
    public string? LOCATION { get; set; }
    // NULL = buổi chung cả lớp; có giá trị = buổi riêng cho 1 nhóm (§5b).
    public int? GROUP_ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class BulkImportSessionRow
{
    public int SESSION_NO { get; set; }
    public DateTime SESSION_DATE { get; set; }
    public string START_TIME { get; set; } = "";
    public string END_TIME { get; set; } = "";
    public string? TOPIC { get; set; }
    public string? LOCATION { get; set; }
    public int? GROUP_ID { get; set; }
}

public class BulkImportSessionsRequest
{
    public int CLASS_ID { get; set; }
    public List<BulkImportSessionRow> ROWS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}

// Dùng để resolve tên nhóm (Excel) sang GROUP_ID khi import buổi học.
public class SimpleGroupListResponse
{
    public bool success { get; set; }
    public List<SimpleGroupItem> data { get; set; } = new();
}

public class SimpleGroupItem
{
    public int ID { get; set; }
    public string GROUP_NAME { get; set; } = "";
}

public class AssignTeacherRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public int IS_PRIMARY { get; set; }
    public int? GROUP_ID { get; set; }          // NULL = dạy cả lớp; có giá trị = phụ trách nhóm đó
    public string LOGIN_USER { get; set; } = "";
}

public class RemoveTeacherRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class AssignStudentRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public List<string>? EMPCDS { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class ApproveEnrollmentRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class RejectEnrollmentRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class SaveGroupRequest
{
    public int? ID { get; set; }
    public int CLASS_ID { get; set; }
    public string GROUP_NAME { get; set; } = "";
    public int? MAX_STUDENTS { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class AutoSplitRequest
{
    public int CLASS_ID { get; set; }
    // Danh sách ID nhóm đã tạo sẵn để chia đều học viên vào — KHÔNG phải số lượng nhóm cần tạo
    // (API HR_api yêu cầu GROUP_IDS của nhóm có sẵn, trước đây field này lệch tên (GROUP_COUNT)
    // nên auto-split luôn nhận GROUP_IDS rỗng và báo lỗi "Cần ≥ 1 group để chia").
    public List<int> GROUP_IDS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}

public class AssignGroupRequest
{
    public int CLASS_ID { get; set; }
    // Bulk — cho phép gán nhiều học viên cùng lúc (trước đây chỉ có EMPCD đơn lẻ, lệch tên với
    // API (EMPCDS) nên request luôn bị bỏ qua toàn bộ, cập nhật 0 dòng nhưng vẫn báo thành công).
    public List<string> EMPCDS { get; set; } = new();
    // NULL = bỏ gán nhóm (unassign) — trước đây khai int không nullable nên không gửi được NULL.
    public int? GROUP_ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class DeleteGroupRequest
{
    public int ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

// ── Class lifecycle ─────────────────────────────────────────
public class ChangeClassStatusRequest
{
    public int ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class ArchiveCourseRequest
{
    public int ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

// ── Session lifecycle ───────────────────────────────────────
public class RescheduleSessionRequest
{
    public int ID { get; set; }
    public DateTime SESSION_DATE { get; set; }
    public string START_TIME { get; set; } = "";
    public string END_TIME { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class CancelSessionRequest
{
    public int ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

// ── Enrollment lifecycle ────────────────────────────────────
public class BulkPreAssignRequest
{
    public int CLASS_ID { get; set; }
    public List<string> EMPCDS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}

public class RecoverEnrollmentRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class RemoveEnrollmentWebRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
}

// ── Certificate ─────────────────────────────────────────────
public class RevokeCertificateRequest
{
    public int CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string REASON { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

// ── Clone & Express ─────────────────────────────────────────
public class CloneFromCourseRequest
{
    public int COURSE_ID { get; set; }
    public string CLASS_NAME { get; set; } = "";
    public DateTime START_DATE { get; set; }
    public string? DESCRIPTION { get; set; }
    public string? PRIMARY_TEACHER_EMPCD { get; set; }
    public List<string> EMPCDS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}

public class ExpressCreateRequest
{
    public int COURSE_ID { get; set; }
    public string CLASS_NAME { get; set; } = "";
    public DateTime SESSION_DATE { get; set; }
    public string START_TIME { get; set; } = "";
    public string END_TIME { get; set; } = "";
    public string? LOCATION { get; set; }
    public string? TOPIC { get; set; }
    public string PRIMARY_TEACHER_EMPCD { get; set; } = "";
    public List<string> EMPCDS { get; set; } = new();
    public int? FINAL_TEST_ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

// §15b Cách 2 — "Sao chép sang đợt mới" từ 1 Class đã có (SCHEDULED/COMPLETED)
public class CloneFromClassRequest
{
    public int SOURCE_CLASS_ID { get; set; }
    public string CLASS_NAME { get; set; } = "";
    public DateTime START_DATE { get; set; }
    public List<string> EMPCDS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}

public class ClassDetailApiResponse
{
    public bool success { get; set; }
    public ClassDetailApiData? data { get; set; }
}

public class ClassDetailApiData
{
    public List<ClassTeacherApiItem>? teachers { get; set; }
}

public class ClassTeacherApiItem
{
    public string EMPCD { get; set; } = "";
    public int IS_PRIMARY { get; set; }
}

public class GetMyReviewApiResponse
{
    public bool success { get; set; }
    public ReviewModelApi? data { get; set; }
}

public class ReviewModelApi
{
    public int CONTENT_RATING { get; set; }
    public int ORGANIZATION_RATING { get; set; }
    public string? FEEDBACK_TEXT { get; set; }
}

public class SessionAttendanceViewApiResponse
{
    public bool success { get; set; }
    public SessionAttendanceViewApiData? data { get; set; }
}

public class SessionAttendanceViewApiData
{
    public List<AttendanceModelApiItem>? ATTENDANCE { get; set; }
}

public class AttendanceModelApiItem
{
    public string EMPCD { get; set; } = "";
    public string? GROUP_NAME { get; set; }
}
