using HR_web.Models.Calendar;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class CalendarService
{
    private readonly ApiService _api;

    public CalendarService(ApiService api)
    {
        _api = api;
    }

    public async Task<CalendarMonthlyResponse> GetMonthlyAsync(string empcd, int year, int month)
    {
        try
        {
            var q = $"empcd={Uri.EscapeDataString(empcd)}&year={year}&month={month}";
            var response = await _api.GetAsync_Raw("calendar/my-monthly", q);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<CalendarMonthlyResponse>(json);
                if (result != null) return result;
            }
            return new CalendarMonthlyResponse { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex)
        {
            return new CalendarMonthlyResponse { success = false, message = ex.Message };
        }
    }
}
