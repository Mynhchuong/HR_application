using HR_web.API;

namespace HR_web.API.Service;

public class MealLockService
{
    private readonly ApiService _api;
    public MealLockService(ApiService api) { _api = api; }

    public async Task<string> ListRawAsync(string? from = null, string? to = null)
    {
        var q = new List<string>();
        if (!string.IsNullOrEmpty(from)) q.Add($"from={Uri.EscapeDataString(from)}");
        if (!string.IsNullOrEmpty(to))   q.Add($"to={Uri.EscapeDataString(to)}");
        var res = await _api.GetAsync_Raw("MealLock", string.Join("&", q));
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    public async Task<string> SaveRawAsync(object payload)
    {
        var res = await _api.PostAsync("MealLock", payload);
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    public async Task<string> DeleteRawAsync(long id)
    {
        var res = await _api.DeleteAsync_Raw($"MealLock/{id}");
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    public async Task<string> CheckRawAsync(string date, string typeMeal = "LUNCH")
    {
        var res = await _api.GetAsync_Raw("MealLock/check",
            $"date={Uri.EscapeDataString(date)}&typeMeal={Uri.EscapeDataString(typeMeal)}");
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"locked\":false}";
    }
}
