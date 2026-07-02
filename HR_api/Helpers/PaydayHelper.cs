namespace HR_api.Helpers;

// Ngày trả lương Samho = ngày 10 hàng tháng.
// Nếu 10 rơi vào T7 → lùi sang T6 (ngày 9).
// Nếu 10 rơi vào CN → lùi sang T6 (ngày 8).
// Expat không nhận lương theo chu kỳ này → luôn trả false.
public static class PaydayHelper
{
    public static DateTime GetPaydayOfMonth(int year, int month)
    {
        var target = new DateTime(year, month, 10);
        return target.DayOfWeek switch
        {
            DayOfWeek.Saturday => target.AddDays(-1),
            DayOfWeek.Sunday   => target.AddDays(-2),
            _                  => target
        };
    }

    public static bool IsPaydayToday(string? roleName = null)
    {
        if (string.Equals(roleName, "Expat", StringComparison.OrdinalIgnoreCase))
            return false;

        var today = DateTime.Today;
        return today == GetPaydayOfMonth(today.Year, today.Month);
    }
}
