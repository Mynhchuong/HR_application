namespace HR_web.API.Service;

/// <summary>
/// Gọi HR_api BulletinShareController (gửi bản tin qua Email / Facebook). Trả nguyên JSON của API cho
/// trang gọi AJAX — không định nghĩa lại model hai bên (API là nơi duy nhất quyết định cấu trúc).
/// </summary>
public class BulletinShareService
{
    private readonly ApiService _api;
    public BulletinShareService(ApiService api) { _api = api; }

    // Mất kết nối giữa chừng: KHÔNG biết mail/bài đã đi chưa → nhắc xem lại trạng thái, tránh gửi trùng
    private const string Fail = "{\"success\":false,\"message\":\"Không nhận được phản hồi từ máy chủ. Tải lại trang và xem trạng thái (Đã gửi/Đã đăng) TRƯỚC khi gửi lại.\"}";

    // API lỗi (500 / trang lỗi HTML / hết hạn API key) → vẫn trả JSON có message để trang hiển thị được
    private static async Task<string> Read(HttpResponseMessage? r)
    {
        if (r == null) return Fail;
        var body = await r.Content.ReadAsStringAsync();
        if (r.IsSuccessStatusCode && body.TrimStart().StartsWith("{")) return body;
        return Fail;
    }

    public async Task<string> GetAsync(string path, string query = "") => await Read(await _api.GetAsync_Raw("BulletinShare/" + path, query));
    public async Task<string> PostAsync(string path, object body) => await Read(await _api.PostAsync("BulletinShare/" + path, body));
}
