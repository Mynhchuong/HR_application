using HR_web.Models.Inquiry;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class InquiryService
{
    private readonly ApiService _api;

    public InquiryService(ApiService api)
    {
        _api = api;
    }

    // GET /apiHR/Inquiry/topics
    public async Task<InquiryTopicsResponse> GetTopicsAsync()
    {
        try
        {
            var res = await _api.GetAsync_Raw("Inquiry/topics");
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryTopicsResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryTopicsResponse { success = false };
            return new InquiryTopicsResponse { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex) { return new InquiryTopicsResponse { success = false, message = ex.Message }; }
    }

    // POST /apiHR/Inquiry/create
    public async Task<InquiryCreateResponse> CreateAsync(
        string  chatType,  string  topicCd,  string? subject,
        string? empCd,     string? anonToken, string  content)
    {
        try
        {
            var payload = new { chatType, topicCd, subject, empCd, anonToken, content };
            var res = await _api.PostAsync("Inquiry/create", payload);
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryCreateResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryCreateResponse { success = false, message = "Lỗi parse response" };
            return new InquiryCreateResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new InquiryCreateResponse { success = false, message = ex.Message }; }
    }

    // GET /apiHR/Inquiry/my?empCd=
    public async Task<InquiryListResponse> GetMyListAsync(string empCd)
    {
        try
        {
            var res = await _api.GetAsync_Raw("Inquiry/my", $"empCd={Uri.EscapeDataString(empCd)}");
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryListResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryListResponse { success = false };
            return new InquiryListResponse { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex) { return new InquiryListResponse { success = false, message = ex.Message }; }
    }

    // GET /apiHR/Inquiry/anon?token=
    public async Task<InquiryListResponse> GetAnonListAsync(string token)
    {
        try
        {
            var res = await _api.GetAsync_Raw("Inquiry/anon", $"token={Uri.EscapeDataString(token)}");
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryListResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryListResponse { success = false };
            return new InquiryListResponse { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex) { return new InquiryListResponse { success = false, message = ex.Message }; }
    }

    // GET /apiHR/Inquiry/messages?id=&afterMsgId=
    public async Task<InquiryMessagesResponse> GetMessagesAsync(long id, long afterMsgId = 0)
    {
        try
        {
            var res = await _api.GetAsync_Raw("Inquiry/messages", $"id={id}&afterMsgId={afterMsgId}");
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryMessagesResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryMessagesResponse { success = false };
            return new InquiryMessagesResponse { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex) { return new InquiryMessagesResponse { success = false, message = ex.Message }; }
    }

    // POST /apiHR/Inquiry/send
    public async Task<InquirySendResponse> SendAsync(
        long    inquiryId,  string? empCd,      string? anonToken,
        string  senderType, string? senderName,  string? content,
        List<InquiryFileInfo>? files = null)
    {
        try
        {
            var payload = new
            {
                inquiryId, empCd, anonToken,
                senderType, senderName, content,
                files = files ?? new List<InquiryFileInfo>()
            };
            var res = await _api.PostAsync("Inquiry/send", payload);
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquirySendResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquirySendResponse { success = false, message = "Lỗi parse response" };
            return new InquirySendResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new InquirySendResponse { success = false, message = ex.Message }; }
    }

    // POST /apiHR/Inquiry/mark-read
    public async Task<InquiryActionResponse> MarkReadAsync(long inquiryId, string readerType, string? anonToken = null)
    {
        try
        {
            var payload = new { inquiryId, readerType, anonToken };
            var res = await _api.PostAsync("Inquiry/mark-read", payload);
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryActionResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryActionResponse { success = false };
            return new InquiryActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new InquiryActionResponse { success = false, message = ex.Message }; }
    }

    // POST /apiHR/Inquiry/recall
    public async Task<InquiryActionResponse> RecallAsync(long msgId, string senderType, string? empCd, string? anonToken)
    {
        try
        {
            var payload = new { msgId, senderType, empCd, anonToken };
            var res = await _api.PostAsync("Inquiry/recall", payload);
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryActionResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryActionResponse { success = false };
            return new InquiryActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new InquiryActionResponse { success = false, message = ex.Message }; }
    }

    // POST /apiHR/Inquiry/close
    public async Task<InquiryActionResponse> CloseAsync(
        long    inquiryId,  string? empCd,      string? anonToken,
        string  closerType, string? closerName,  string? closeNote)
    {
        try
        {
            var payload = new { inquiryId, empCd, anonToken, closerType, closerName, closeNote };
            var res = await _api.PostAsync("Inquiry/close", payload);
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryActionResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryActionResponse { success = false };
            return new InquiryActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new InquiryActionResponse { success = false, message = ex.Message }; }
    }

    // POST /apiHR/Inquiry/unlock  — chỉ Admin
    public async Task<InquiryActionResponse> UnlockAsync(long inquiryId, string adminEmpCd, string adminName)
    {
        try
        {
            var payload = new { inquiryId, adminEmpCd, adminName };
            var res = await _api.PostAsync("Inquiry/unlock", payload);
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryActionResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryActionResponse { success = false };
            return new InquiryActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new InquiryActionResponse { success = false, message = ex.Message }; }
    }

    // GET /apiHR/Inquiry/hr-list
    public async Task<InquiryHrListResponse> GetHrListAsync(
        string? status     = null,
        string? topicCd    = null,
        string? chatType   = null,
        string? assignedTo = null,
        string? search     = null,
        int     page       = 1,
        int     pageSize   = 30)
    {
        try
        {
            var q = new List<string>();
            if (!string.IsNullOrEmpty(status))     q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(topicCd))    q.Add($"topicCd={Uri.EscapeDataString(topicCd)}");
            if (!string.IsNullOrEmpty(chatType))   q.Add($"chatType={Uri.EscapeDataString(chatType)}");
            if (!string.IsNullOrEmpty(assignedTo)) q.Add($"assignedTo={Uri.EscapeDataString(assignedTo)}");
            if (!string.IsNullOrEmpty(search))     q.Add($"search={Uri.EscapeDataString(search)}");
            q.Add($"page={page}");
            q.Add($"pageSize={pageSize}");
            var res = await _api.GetAsync_Raw("Inquiry/hr-list", string.Join("&", q));
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryHrListResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryHrListResponse { success = false };
            return new InquiryHrListResponse { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex) { return new InquiryHrListResponse { success = false, message = ex.Message }; }
    }

    // GET /apiHR/Inquiry/report
    public async Task<InquiryReportResponse> GetReportAsync(string type, int year, int week, int month)
    {
        try
        {
            var q = $"type={type}&year={year}&week={week}&month={month}";
            var res = await _api.GetAsync_Raw("Inquiry/report", q);
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryReportResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryReportResponse { success = false };
            return new InquiryReportResponse { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex) { return new InquiryReportResponse { success = false, message = ex.Message }; }
    }

    // POST /apiHR/Inquiry/rate
    public async Task<InquiryActionResponse> RateAsync(
        long    inquiryId, string? empCd, string? anonToken,
        int     rating,    string? ratingNote)
    {
        try
        {
            var payload = new { inquiryId, empCd, anonToken, rating, ratingNote };
            var res = await _api.PostAsync("Inquiry/rate", payload);
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<InquiryActionResponse>(await res.Content.ReadAsStringAsync())
                       ?? new InquiryActionResponse { success = false };
            return new InquiryActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new InquiryActionResponse { success = false, message = ex.Message }; }
    }
}
