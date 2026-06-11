using HR_web.Models.Menu;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class MenuService
{
    private readonly ApiService _api;

    public MenuService(ApiService api) { _api = api; }

    private class Resp<T>
    {
        public bool    success  { get; set; }
        public T?      data     { get; set; }
        public string? message  { get; set; }
        public object? weekInfo { get; set; }
    }

    private async Task<(bool success, string message)> PostResult(string url, object body)
    {
        var res = await _api.PostAsync(url, body);
        if (res == null) return (false, "Lỗi kết nối server");
        var parsed = JsonConvert.DeserializeObject<Resp<object>>(await res.Content.ReadAsStringAsync());
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server");
    }

    // ── Week ──────────────────────────────────────────────────────────────────
    public async Task<List<MenuWeekModel>> GetWeekListAsync()
    {
        var r = await _api.GetAsync<Resp<List<MenuWeekModel>>>("MenuWeek/list");
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<(MenuWeekModel? week, List<MenuDetailModel> details)> GetWeekByIdAsync(int id)
    {
        var r = await _api.GetAsync<Resp<MenuWeekModel>>($"MenuWeek/{id}");
        if (r?.success != true) return (null, new());
        var details = JsonConvert.DeserializeObject<List<MenuDetailModel>>(
            JsonConvert.SerializeObject(
                ((Newtonsoft.Json.Linq.JObject)JsonConvert.DeserializeObject(
                    JsonConvert.SerializeObject(r))!).GetValue("details"))) ?? new();
        return (r.data, details);
    }

    public async Task<(bool success, string message)> SaveWeekAsync(SaveWeekRequest req)
        => await PostResult("MenuWeek/save", req);

    public async Task<(bool success, string message)> SaveDetailAsync(SaveDetailRequest req)
        => await PostResult("MenuWeek/detail/save", req);

    public async Task<(bool success, string message)> PublishAsync(int id, string loginUser)
        => await PostResult($"MenuWeek/publish?id={id}&loginUser={loginUser}", new { });

    public async Task<(bool success, string message)> UnpublishAsync(int id, string loginUser)
        => await PostResult($"MenuWeek/unpublish?id={id}&loginUser={loginUser}", new { });

    public async Task<(bool success, string message)> DeleteWeekAsync(int id)
        => await PostResult($"MenuWeek/delete?id={id}", new { });

    // ── Food ──────────────────────────────────────────────────────────────────
    public async Task<List<MenuFoodModel>> GetFoodListAsync()
    {
        var r = await _api.GetAsync<Resp<List<MenuFoodModel>>>("MenuFood/list");
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<List<MenuFoodModel>> GetActiveFoodsAsync()
    {
        var r = await _api.GetAsync<Resp<List<MenuFoodModel>>>("MenuFood/active");
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<(bool success, string message, int newId)> SaveFoodAsync(SaveFoodRequest req)
    {
        var res = await _api.PostAsync("MenuFood/save", req);
        if (res == null) return (false, "Lỗi kết nối server", 0);
        var json   = await res.Content.ReadAsStringAsync();
        var parsed = JsonConvert.DeserializeObject<Resp<object>>(json);
        var jobj   = Newtonsoft.Json.Linq.JObject.Parse(json);
        int newId  = jobj["newId"]?.Value<int>() ?? 0;
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server", newId);
    }

    public async Task<(bool success, string message)> ToggleFoodAsync(int id, string loginUser)
        => await PostResult($"MenuFood/toggle?id={id}&loginUser={loginUser}", new { });

    public async Task<(bool success, string message)> DeleteFoodAsync(int id)
        => await PostResult($"MenuFood/delete?id={id}", new { });

    // ── Worker view ───────────────────────────────────────────────────────────
    public async Task<List<MenuDayViewModel>> GetTodayMenuAsync()
    {
        var r = await _api.GetAsync<Resp<List<MenuDayViewModel>>>("MenuWeek/today");
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<List<MenuDayViewModel>> GetThisWeekMenuAsync()
    {
        var r = await _api.GetAsync<Resp<List<MenuDayViewModel>>>("MenuWeek/this-week");
        return r?.success == true ? r.data ?? new() : new();
    }
}
