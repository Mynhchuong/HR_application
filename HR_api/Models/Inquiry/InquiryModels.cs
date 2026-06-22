namespace HR_api.Models.Inquiry;

// ─── REQUEST MODELS ───────────────────────────────────────────────

public class InquiryCreateRequest
{
    public string  ChatType   { get; set; } = "DIRECT"; // DIRECT | ANON
    public string  TopicCd    { get; set; } = "";
    public string? Subject    { get; set; }
    public string? EmpCd      { get; set; }             // DIRECT: empcd; ANON: null
    public string? AnonToken  { get; set; }             // ANON: UUID từ localStorage
    public string  Content    { get; set; } = "";       // tin nhắn đầu tiên bắt buộc
}

public class InquirySendRequest
{
    public long    InquiryId  { get; set; }
    public string? EmpCd      { get; set; }
    public string? AnonToken  { get; set; }
    public string  SenderType { get; set; } = "EMP";   // EMP | HR
    public string? SenderName { get; set; }             // HR_web tự điền từ session
    public string? Content    { get; set; }
    public List<InquiryFileInfo> Files { get; set; } = new();
    public List<InquiryRefInfo>  Refs  { get; set; } = new();   // trích dẫn Policy/Guide (HR/Admin only)
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
}

public class InquiryMsgDto
{
    public long    Id          { get; set; }
    public long    InquiryId   { get; set; }
    public string  SenderType  { get; set; } = "";
    public string? SenderCd    { get; set; }
    public string? SenderName  { get; set; }
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
    public double? AvgRating     { get; set; }
    public double? AvgMsg        { get; set; }
    public int     ClosedByHr    { get; set; }
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
    public double? AvgRating { get; set; }
}

public class InquiryReportHrRowDto
{
    public string? HrCd         { get; set; }
    public string? HrName       { get; set; }
    public int     Handled      { get; set; }
    public int     CntClosed    { get; set; }
    public double? AvgRating    { get; set; }
    public double? AvgHandleMin { get; set; }
}
