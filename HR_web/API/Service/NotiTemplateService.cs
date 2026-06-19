using HR_web.Models.NotiTemplate;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class NotiTemplateService
{
    private readonly ApiService _api;

    public NotiTemplateService(ApiService api) { _api = api; }

    private class Resp<T>
    {
        public bool    success { get; set; }
        public T?      data    { get; set; }
        public string? message { get; set; }
    }

    public async Task<List<NotiTemplateModel>> GetListAsync()
    {
        var r = await _api.GetAsync<Resp<List<NotiTemplateModel>>>("NotiTemplate/list");
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<(bool success, string message)> SaveAsync(SaveNotiTemplateRequest req, string loginUser)
    {
        var body = new
        {
            TEMPLATE_KEY = req.TEMPLATE_KEY,
            TITLE_VI     = req.TITLE_VI,
            BODY_VI      = req.BODY_VI,
            TITLE_EN     = req.TITLE_EN,
            BODY_EN      = req.BODY_EN,
            LOGIN_USER   = loginUser
        };
        var res = await _api.PostAsync("NotiTemplate/save", body);
        if (res == null) return (false, "Lỗi kết nối server");
        var parsed = JsonConvert.DeserializeObject<Resp<object>>(await res.Content.ReadAsStringAsync());
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server");
    }

    public async Task<(bool success, string message)> ToggleAsync(string key, string loginUser)
    {
        var res = await _api.PostAsync($"NotiTemplate/toggle?key={key}&loginUser={Uri.EscapeDataString(loginUser)}", new { });
        if (res == null) return (false, "Lỗi kết nối server");
        var parsed = JsonConvert.DeserializeObject<Resp<object>>(await res.Content.ReadAsStringAsync());
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server");
    }
}
