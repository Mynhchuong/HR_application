using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace HR_web.Models.Inquiry;

// ─── DTOs (mirror HR_api output — deserialized from JSON) ────────────────────

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class InquiryTopicDto
{
    public string  TopicCd     { get; set; } = "";
    public string  TopicName   { get; set; } = "";
    public string? TopicNameEn { get; set; }
    public string? Icon        { get; set; }
    public string? Color       { get; set; }
    public int     SortOrder   { get; set; }
    public bool    IsActive    { get; set; } = true;
    public int     UsageCount  { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class InquiryListItemDto
{
    public long      Id           { get; set; }
    public string    InquiryNo    { get; set; } = "";
    public string    ChatType     { get; set; } = "";
    public string    TopicCd      { get; set; } = "";
    public string?   TopicName    { get; set; }
    public string?   TopicColor   { get; set; }
    public string?   TopicIcon    { get; set; }
    public string?   Subject      { get; set; }
    public string?   EmpCd        { get; set; }
    public string?   EmpName      { get; set; }
    public string?   DeptName     { get; set; }
    public string?   LineName     { get; set; }
    public string?   WorkName     { get; set; }
    public string?   AnonDisplay  { get; set; }
    public string?   AnonToken    { get; set; }
    public string    Status       { get; set; } = "";
    public string?   AssignedTo   { get; set; }
    public string?   AssignedName { get; set; }
    public int       UnreadHr     { get; set; }
    public int       UnreadEmp    { get; set; }
    public int       MsgCount     { get; set; }
    public DateTime? LastMsgDt    { get; set; }
    public DateTime? InstDt       { get; set; }
    public DateTime? ClosedDt     { get; set; }
    public string?   ClosedByName { get; set; }
    public string?   ClosedByType { get; set; }
    public string?   CloseNote    { get; set; }
    public DateTime? LockedDt     { get; set; }
    public int?      Rating       { get; set; }
    public string?   RatingNote   { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class InquiryMsgDto
{
    public long      Id          { get; set; }
    public long      InquiryId   { get; set; }
    public string    SenderType  { get; set; } = "";
    public string?   SenderCd    { get; set; }
    public string?   SenderName  { get; set; }
    public string    MsgType     { get; set; } = "TEXT";
    public string?   Content     { get; set; }
    public bool      IsReadHr    { get; set; }
    public bool      IsReadEmp   { get; set; }
    public bool      IsDeleted   { get; set; }
    public DateTime  SentDt      { get; set; }
    public List<InquiryAttachDto> Attachments { get; set; } = new();
    public List<InquiryRefDto>    Refs        { get; set; } = new();
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class InquiryRefDto
{
    public long    Id       { get; set; }
    public string  RefType  { get; set; } = "";   // POLICY | GUIDE
    public long    RefId    { get; set; }
    public string? RefTitle { get; set; }
}

public class InquiryRefInfo
{
    public string RefType { get; set; } = "POLICY";
    public long   RefId   { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class InquiryAttachDto
{
    public long    Id        { get; set; }
    public string  FileName  { get; set; } = "";
    public string  FilePath  { get; set; } = "";
    public string  FileType  { get; set; } = "";
    public string? MimeType  { get; set; }
    public long?   FileSize  { get; set; }
    public string? ThumbPath { get; set; }
}

public class InquiryFileInfo
{
    public string  FileName  { get; set; } = "";
    public string  FilePath  { get; set; } = "";
    public string  FileType  { get; set; } = "IMAGE";
    public string? MimeType  { get; set; }
    public long?   FileSize  { get; set; }
    public string? ThumbPath { get; set; }
}

// ─── Response wrappers ───────────────────────────────────────────────────────

public class InquiryTopicsResponse
{
    public bool   success { get; set; }
    public string? message { get; set; }
    public List<InquiryTopicDto> data { get; set; } = new();
}

public class InquiryListResponse
{
    public bool   success  { get; set; }
    public string? message { get; set; }
    public List<InquiryListItemDto> data { get; set; } = new();
}

public class InquiryHrListResponse
{
    public bool   success     { get; set; }
    public string? message    { get; set; }
    public List<InquiryListItemDto> data { get; set; } = new();
    public int page           { get; set; }
    public int pageSize       { get; set; }
    public int total          { get; set; }
    public int totalPages     { get; set; }
    public int cntOpen        { get; set; }
    public int cntClosed      { get; set; }
    public int totalUnread    { get; set; }
}

public class InquiryMessagesResponse
{
    public bool   success  { get; set; }
    public string? message { get; set; }
    public InquiryListItemDto? inquiry  { get; set; }
    public List<InquiryMsgDto> messages { get; set; } = new();
}

public class InquiryCreateResponse
{
    public bool    success    { get; set; }
    public string? message    { get; set; }
    public long    inquiryId  { get; set; }
    public string? inquiryNo  { get; set; }
}

public class InquirySendResponse
{
    public bool    success    { get; set; }
    public string? message    { get; set; }
    public long    msgId      { get; set; }
    public string? lockedBy   { get; set; }
    public bool    justLocked { get; set; }
}

public class InquiryActionResponse
{
    public bool    success  { get; set; }
    public string? message  { get; set; }
}

// ─── REPORT models ───────────────────────────────────────────────────────────

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class InquiryReportSummary
{
    public int     total         { get; set; }
    public int     cntOpen       { get; set; }
    public int     cntClosed     { get; set; }
    public int     cntDirect     { get; set; }
    public int     cntAnon       { get; set; }
    public double? avgRating     { get; set; }
    public double? avgMsg        { get; set; }
    public int     closedByHr    { get; set; }
    public int     closedByEmp   { get; set; }
    public int     closedByAdmin { get; set; }
    public double? avgHandleMin  { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class InquiryReportTopicRow
{
    public string  topicCd   { get; set; } = "";
    public string? topicName { get; set; }
    public string? color     { get; set; }
    public string? icon      { get; set; }
    public int     total     { get; set; }
    public int     cntOpen   { get; set; }
    public int     cntClosed { get; set; }
    public double? avgRating { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class InquiryReportHrRow
{
    public string? hrCd         { get; set; }
    public string? hrName       { get; set; }
    public int     handled      { get; set; }
    public int     cntClosed    { get; set; }
    public double? avgRating    { get; set; }
    public double? avgHandleMin { get; set; }
}

public class InquiryReportResponse
{
    public bool                        success  { get; set; }
    public string?                     message  { get; set; }
    public InquiryReportSummary?       summary  { get; set; }
    public List<InquiryReportTopicRow> byTopic  { get; set; } = new();
    public List<InquiryReportHrRow>    byHr     { get; set; } = new();
}
