namespace HR_api.Models.Inquiry;

// ─── REQUEST MODELS ───────────────────────────────────────────────

public class InquiryCreateRequest
{
    public string  ChatType   { get; set; } = "DIRECT"; // DIRECT | ANON
    public string  TopicCd    { get; set; } = "";
    public string? Subject    { get; set; }
    public string? EmpCd      { get; set; }             // DIRECT: empcd; ANON: null
    public string? AnonToken  { get; set; }             // ANON: UUID từ localStorage
    public string? Content    { get; set; }             // tin nhắn đầu tiên — CÓ THỂ NULL từ
                                                        // 2026-11-12 (tạo hội thoại rỗng rồi chat)
    public string? ReportType { get; set; }             // GRIEVANCE (Kiến nghị) | OPINION (Ý kiến); null = web bản cũ chưa gửi
}

public class InquiryLookupByCodeRequest
{
    public string Code { get; set; } = "";
}

public class InquirySendRequest
{
    public long    InquiryId  { get; set; }
    public string? EmpCd      { get; set; }
    public string? AnonToken  { get; set; }
    public string  SenderType   { get; set; } = "EMP";   // EMP | HR
    public string? SenderName   { get; set; }             // hiển thị trong chat (role khi là HR/Admin/CSR)
    public string? AssignedName { get; set; }             // họ tên đầy đủ, dùng để lock/report (ASSIGNED_NAME)
    public string? Content      { get; set; }
    public List<InquiryFileInfo> Files { get; set; } = new();
    public List<InquiryRefInfo>  Refs  { get; set; } = new();   // trích dẫn Policy/Guide (HR/Admin only)
    public bool    SkipLockCheck { get; set; }                  // chỉ web đặt khi CSR duyệt bản nháp: bỏ qua luật "1 người phụ trách" (không chuyển lock)
}

public class InquiryRefInfo
{
    public string RefType { get; set; } = "POLICY";  // POLICY | GUIDE
    public long   RefId   { get; set; }
}

public class InquiryFileInfo
{
    public string  FileName  { get; set; } = "";
    public string  FilePath  { get; set; } = "";        // relative path on share
    public string  FileType  { get; set; } = "IMAGE";  // IMAGE | FILE
    public string? MimeType  { get; set; }
    public long?   FileSize  { get; set; }
    public string? ThumbPath { get; set; }
}

public class InquiryCloseRequest
{
    public long    InquiryId    { get; set; }
    public string? EmpCd        { get; set; }
    public string? AnonToken    { get; set; }
    public string  CloserType   { get; set; } = "HR";  // HR | EMP
    public string? CloserName   { get; set; }
    public string? CloseNote    { get; set; }
}

public class InquiryBulkCloseRequest
{
    public List<string> InquiryNos { get; set; } = new();
    public string? EmpCd      { get; set; }
    public string? CloserName { get; set; }
    public string  CloserType { get; set; } = "HR";  // HR | CSR — dùng cho CLOSED_BY_TYPE để báo cáo tách ra
    public string? Message    { get; set; }
}

public class InquiryBulkCloseItemResult
{
    public string  InquiryNo { get; set; } = "";
    public bool    Success   { get; set; }
    public string? Message   { get; set; }
}

public class InquiryUnlockRequest
{
    public long   InquiryId  { get; set; }
    public string AdminEmpCd { get; set; } = "";
    public string AdminName  { get; set; } = "";
}

public class InquiryMarkReadRequest
{
    public long    InquiryId { get; set; }
    public string  ReaderType { get; set; } = "EMP";   // EMP | HR
    public string? AnonToken  { get; set; }
    // Mã NV của người đang xem (khi ReaderType=HR) — CSR/HR/Admin xem chung 1 hội thoại nhưng phải
    // tính "đã đọc" RIÊNG cho từng người, không dùng chung 1 cờ (bug 2026-09-10: 1 acc đọc, các acc
    // còn lại tự nhiên bị đánh dấu đã đọc theo dù chưa mở).
    public string? ViewerEmpcd { get; set; }
}

public class InquiryRatingRequest
{
    public long    InquiryId  { get; set; }
    public string? EmpCd      { get; set; }
    public string? AnonToken  { get; set; }
    public int     Rating     { get; set; }             // 1-5
    public string? RatingNote { get; set; }
}

// ─── RESPONSE / DTO MODELS ───────────────────────────────────────

public class InquiryTopicDto
{
    public string  TopicCd      { get; set; } = "";
    public string  TopicName    { get; set; } = "";
    public string? TopicNameEn  { get; set; }
    public string? Icon         { get; set; }
    public string? Color        { get; set; }
    public int     SortOrder    { get; set; }
    public bool    IsActive     { get; set; } = true;
    public int     UsageCount   { get; set; }
    // Catalog Grievance (2026-11-06): mức độ + số ngày phản hồi (từ HR_INQUIRY_SEVERITY) + PIC (phòng ban phụ trách).
    // PicDeptRoleId/PicDeptName chỉ trả ở endpoint ADMIN — GetTopics (công nhân) KHÔNG trả, đó là thông tin điều phối nội bộ.
    public string  SeverityCd     { get; set; } = "LOW";
    public string? SeverityName   { get; set; }
    public string? SeverityColor  { get; set; }
    public int?    ResponseDays   { get; set; }
    public int?    PicDeptRoleId  { get; set; }
    public string? PicDeptName    { get; set; }
}

public class InquiryTopicSaveRequest
{
    public string  TopicCd      { get; set; } = "";
    public string  TopicName    { get; set; } = "";
    public string? TopicNameEn  { get; set; }
    public string? Icon         { get; set; }
    public string? Color        { get; set; }
    public int     SortOrder    { get; set; }
    public bool    IsActive     { get; set; } = true;
    public string? UpdtId       { get; set; }
    public string? SeverityCd   { get; set; }           // LOW | HIGH | ZT (HR_INQUIRY_SEVERITY); trống = LOW
    public int?    PicDeptRoleId { get; set; }          // HR_DEPT_ROLES.ID; null = chưa gán
}

public class InquiryTopicToggleRequest
{
    public bool    IsActive     { get; set; }
    public string? UpdtId       { get; set; }
}

public class InquiryListItemDto
{
    public long    Id           { get; set; }
    public string  InquiryNo    { get; set; } = "";
    public string  ChatType     { get; set; } = "";
    public string  TopicCd      { get; set; } = "";
    public string? TopicName    { get; set; }
    public string? TopicColor   { get; set; }
    public string? TopicIcon    { get; set; }
    public string? Subject      { get; set; }
    public string? EmpCd        { get; set; }
    public string? EmpName      { get; set; }
    public string? DeptName     { get; set; }
    public string? LineName     { get; set; }
    public string? WorkName     { get; set; }
    public string? AnonDisplay  { get; set; }
    public string? AnonToken    { get; set; }   // chỉ có trong GetMessages (validate ownership)
    public string  Status       { get; set; } = "";
    public string? AssignedTo   { get; set; }
    public string? AssignedName { get; set; }
    public int     UnreadHr     { get; set; }
    public int     UnreadEmp    { get; set; }
    public int     MsgCount     { get; set; }
    public DateTime? LastMsgDt  { get; set; }
    public DateTime? InstDt     { get; set; }
    public DateTime? ClosedDt   { get; set; }
    public string? ClosedByName { get; set; }
    public string? ClosedByType { get; set; }
    public string? CloseNote    { get; set; }
    public DateTime? LockedDt   { get; set; }
    public int?    Rating       { get; set; }
    public string? RatingNote   { get; set; }
    public int     DraftPending { get; set; }   // số bản nháp trả lời đang chờ CSR duyệt (chỉ có trong hr-list)
}

public class InquiryMsgDto
{
    public long    Id          { get; set; }
    public long    InquiryId   { get; set; }
    public string  SenderType  { get; set; } = "";
    public string? SenderCd    { get; set; }
    public string? SenderName  { get; set; }
    /// <summary>Chức vụ người gửi (HR | CSR | GA...) — tra từ HR_USERS.DEPT_ROLE_ID theo SENDER_CD.
    /// null khi người gửi là công nhân, tin hệ thống, hoặc DB chưa có cột dept role.</summary>
    public string? SenderRole  { get; set; }
    public string  MsgType     { get; set; } = "TEXT";
    public string? Content     { get; set; }
    public bool    IsReadHr    { get; set; }
    public bool    IsReadEmp   { get; set; }
    public bool    IsDeleted   { get; set; }
    public DateTime SentDt     { get; set; }
    public List<InquiryAttachDto> Attachments { get; set; } = new();
    public List<InquiryRefDto>    Refs        { get; set; } = new();
}

public class InquiryRefDto
{
    public long    Id       { get; set; }
    public string  RefType  { get; set; } = "";   // POLICY | GUIDE
    public long    RefId    { get; set; }
    public string? RefTitle { get; set; }
}

public class InquiryAttachDto
{
    public long    Id          { get; set; }
    public string  FileName    { get; set; } = "";
    public string  FilePath    { get; set; } = "";
    public string  FileType    { get; set; } = "";
    public string? MimeType    { get; set; }
    public long?   FileSize    { get; set; }
    public string? ThumbPath   { get; set; }
}

// ─── REPORT DTOs ─────────────────────────────────────────────────

public class InquiryReportSummaryDto
{
    public int     Total         { get; set; }
    public int     CntOpen       { get; set; }
    public int     CntClosed     { get; set; }
    public int     CntDirect     { get; set; }
    public int     CntAnon       { get; set; }
    public int     CntSatisfied    { get; set; }   // RATING >= 4  (Hài lòng)
    public int     CntNormal       { get; set; }   // RATING = 3   (Bình thường)
    public int     CntNotSatisfied { get; set; }   // RATING <= 2  (Không hài lòng)
    public int     CntUnrated      { get; set; }   // đã đóng nhưng chưa đánh giá → tính Hài lòng (quy tắc khách: để trống = Satisfied)
    public double? AvgRating     { get; set; }
    public double? AvgMsg        { get; set; }
    public int     ClosedByHr    { get; set; }
    public int     ClosedByCsr   { get; set; }
    public int     ClosedByEmp   { get; set; }
    public int     ClosedByAdmin { get; set; }
    public double? AvgHandleMin  { get; set; }
}

public class InquiryReportTopicRowDto
{
    public string  TopicCd   { get; set; } = "";
    public string? TopicName { get; set; }
    public string? Color     { get; set; }
    public string? Icon      { get; set; }
    public int     Total     { get; set; }
    public int     CntOpen   { get; set; }
    public int     CntClosed { get; set; }
    public int     CntSatisfied    { get; set; }   // RATING >= 4  (Hài lòng)
    public int     CntNormal       { get; set; }   // RATING = 3   (Bình thường)
    public int     CntNotSatisfied { get; set; }   // RATING <= 2  (Không hài lòng)
    public int     CntUnrated      { get; set; }   // đã đóng nhưng chưa đánh giá → tính Hài lòng (quy tắc khách: để trống = Satisfied)
    public double? AvgRating { get; set; }
}

public class InquiryReportHrRowDto
{
    public string? HrCd         { get; set; }
    public string? HrName       { get; set; }
    public int     Handled      { get; set; }
    public int     CntClosed    { get; set; }
    public int     CntSatisfied    { get; set; }   // RATING >= 4  (Hài lòng)
    public int     CntNormal       { get; set; }   // RATING = 3   (Bình thường)
    public int     CntNotSatisfied { get; set; }   // RATING <= 2  (Không hài lòng)
    public int     CntUnrated      { get; set; }   // đã đóng nhưng chưa đánh giá → tính Hài lòng (quy tắc khách: để trống = Satisfied)
    public double? AvgRating    { get; set; }
    public double? AvgHandleMin { get; set; }
}

public class InquiryReportRawRowDto
{
    public long      Id             { get; set; }
    public string    InquiryNo      { get; set; } = "";
    public string?   Subject        { get; set; }          // tiêu đề tuỳ chọn NV tự đặt (HR_INQUIRY.SUBJECT)
    public string?   TopicName      { get; set; }
    public string    ChatType       { get; set; } = "";   // DIRECT | ANON
    public string    Status         { get; set; } = "";
    public DateTime? InstDt         { get; set; }
    public DateTime? ClosedDt       { get; set; }
    public string?   ClosedByName   { get; set; }
    public string?   ClosedByType   { get; set; }         // HR | EMP | ADMIN
    public string?   CloseNote      { get; set; }
    public string?   EmpCd          { get; set; }          // rỗng nếu ẩn danh
    public string?   EmpDisplay     { get; set; }   // "Ẩn danh" nếu CHAT_TYPE = ANON
    public string?   DeptName       { get; set; }
    public string?   LineName       { get; set; }
    public string?   WorkName       { get; set; }
    public string?   FirstMsg       { get; set; }
    public int       MsgCount       { get; set; }
    public string?   AssignedName   { get; set; }
    public string?   AssignedTo     { get; set; }
    public string?   LastHandlerMsg { get; set; }
    public double?   ResponseMin    { get; set; }   // LOCKED_DT - INST_DT
    public int?      Rating         { get; set; }
    public string?   RatingNote     { get; set; }
    public int       HrMsgCount     { get; set; }   // số tin trả lời của HR/GA/CSR (0 = chưa ai phản hồi)
    public DateTime? DeadlineDt     { get; set; }
    public string?   PicName        { get; set; }
    public string?   ReportType     { get; set; }          // GRIEVANCE | OPINION | null (đơn cũ chưa phân loại)
    public string?   SeverityName   { get; set; }          // tên mức độ tiếng Việt (HR_INQUIRY_SEVERITY.NAME_VI)
    public int       DraftPending   { get; set; }   // bản nháp đang chờ CSR duyệt
    public string?   Sex            { get; set; }   // M | F (ECM100.SEXGB); null nếu ẩn danh
}

// ── Chi tiết case cho CSR (trang HrInquiry/CaseDetail) ───────────────────────
public class InquiryCaseInfoUpdateRequest
{
    public string?   ReportType    { get; set; }        // GRIEVANCE | OPINION
    public string?   TopicCd       { get; set; }
    public string?   SeverityCd    { get; set; }
    public int?      PicDeptRoleId { get; set; }
    public DateTime? DeadlineDt    { get; set; }
    public string?   UpdtId        { get; set; }
}


// ─── Bản nháp trả lời của HR/GA chờ CSR duyệt (HR_INQUIRY_DRAFT) ───────────────
public class InquiryDraftCreateRequest
{
    public long    InquiryId  { get; set; }
    public string  AuthorCd   { get; set; } = "";
    public string? AuthorName { get; set; }
    public string? AuthorRole { get; set; }     // HR | GA
    public string? Content    { get; set; }
    public string? FilesJson  { get; set; }
    public string? RefsJson   { get; set; }
}

public class InquiryDraftReviewRequest
{
    public string? ReviewerCd   { get; set; }
    public string? ReviewerName { get; set; }
    public string? Content      { get; set; }   // claim: nội dung CSR đã sửa (null = giữ nguyên bản nháp)
    public string? Note         { get; set; }   // return: góp ý
    public long?   MsgId        { get; set; }   // finalize: tin nhắn thật đã gửi
    public string? ActorCd      { get; set; }   // dismiss: người thu hồi (phải là người soạn)
}


// ─── Trang "Phân loại nhanh" (CSR) ──────────────────────────────────────────────
public class TriageConfirmRequest
{
    public List<long> Ids           { get; set; } = new();
    public string?    ReportType    { get; set; }          // GRIEVANCE | OPINION; null = giữ nguyên
    public string?    TopicCd       { get; set; }          // null = giữ nguyên
    public string?    SeverityCd    { get; set; }          // null = giữ nguyên
    public int?       PicDeptRoleId { get; set; }
    public bool       SetPic        { get; set; }          // true = ghi PIC (kể cả khi chọn "chưa gán" → giữ nguyên nếu PicDeptRoleId null)
    public string?    DeadlineMode  { get; set; }          // created (ngày tạo + số ngày) | today (hôm nay + số ngày) | keep
    public string?    UpdtId        { get; set; }
}
