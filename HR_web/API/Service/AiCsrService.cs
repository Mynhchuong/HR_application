using HR_web.API;

namespace HR_web.API.Service;

// Passthrough sang HR_api (apiHR/AiCsr/*) cho tính năng "AI SAMHO - CSR".
// Trả raw JSON string để Controller Content(...) thẳng ra client (pattern giống InquiryService).
public class AiCsrService
{
    private readonly ApiService _api;
    public AiCsrService(ApiService api) { _api = api; }

    private const string Err = "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";

    private static async Task<string> ReadAsync(HttpResponseMessage? res)
        => res?.IsSuccessStatusCode == true ? await res.Content.ReadAsStringAsync() : Err;

    // ── Nhân viên ──────────────────────────────────────────────
    public Task<string> AskRawAsync(object payload)          => Post("AiCsr/ask", payload);
    public Task<string> NewThreadRawAsync(object payload)    => Post("AiCsr/new-thread", payload);
    public Task<string> MarkReadEmpRawAsync(object payload)  => Post("AiCsr/mark-read-emp", payload);

    public async Task<string> MessagesRawAsync(long chatId, string empcd, long afterMsgId)
    {
        var qs = $"chatId={chatId}&empcd={Uri.EscapeDataString(empcd)}&afterMsgId={afterMsgId}";
        return await ReadAsync(await _api.GetAsync_Raw("AiCsr/messages", qs));
    }

    // Đoạn chat OPEN hiện tại của NV + toàn bộ tin nhắn — dùng lúc mở trang.
    public async Task<string> MyThreadRawAsync(string empcd)
        => await ReadAsync(await _api.GetAsync_Raw("AiCsr/my-thread", $"empcd={Uri.EscapeDataString(empcd)}"));

    // ── CSR / Admin ───────────────────────────────────────────
    public async Task<string> ListRawAsync(string? status, string? search, int page, int pageSize)
    {
        var q = new List<string>();
        if (!string.IsNullOrEmpty(status)) q.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrEmpty(search)) q.Add($"search={Uri.EscapeDataString(search)}");
        q.Add($"page={page}");
        q.Add($"pageSize={pageSize}");
        return await ReadAsync(await _api.GetAsync_Raw("AiCsr/list", string.Join("&", q)));
    }

    public async Task<string> ThreadRawAsync(long chatId)
        => await ReadAsync(await _api.GetAsync_Raw("AiCsr/thread", $"chatId={chatId}"));

    public async Task<string> ThreadMessagesRawAsync(long chatId, long afterMsgId)
        => await ReadAsync(await _api.GetAsync_Raw("AiCsr/thread-messages", $"chatId={chatId}&afterMsgId={afterMsgId}"));

    public Task<string> CsrReplyRawAsync(object payload)     => Post("AiCsr/csr-reply", payload);
    public Task<string> CsrMarkReadRawAsync(object payload)  => Post("AiCsr/csr-mark-read", payload);
    public Task<string> SetStatusRawAsync(object payload)    => Post("AiCsr/set-status", payload);

    private async Task<string> Post(string ep, object payload) => await ReadAsync(await _api.PostAsync(ep, payload));
}
