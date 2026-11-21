using System.Collections.Concurrent;

namespace HR_api.Helpers;

/// <summary>
/// Chống dò mật khẩu (yêu cầu bảo mật 2026-11-21): sai 5 lần trong 15 phút → tạm khoá mã thẻ đó 15 phút.
/// Trước đây đăng nhập không giới hạn số lần sai, mà mã thẻ dễ đoán (năm-tháng-ngày + số thứ tự) → thử mật khẩu
/// liên tục được. Đếm theo MÃ THẺ (HR_web gọi API từ server nên không phân biệt được IP người dùng).
/// Lưu trong bộ nhớ: khởi động lại HR_api là xoá hết lượt đếm (chấp nhận được — chỉ để chặn dò hàng loạt).
/// Kiểm tra khoá chạy TRƯỚC khi đọc DB, nên khi đang khoá có đúng mật khẩu cũng không vào được.
/// </summary>
public static class LoginThrottle
{
    public const int MaxFails = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);

    private sealed class Entry { public int Fails; public DateTime FirstFailUtc; public DateTime? LockedUntilUtc; }
    private static readonly ConcurrentDictionary<string, Entry> _map = new();
    private static DateTime _nextCleanUtc = DateTime.UtcNow;

    private static string Key(string empcd) => empcd.Trim().ToUpperInvariant();

    /// <summary>Đang bị khoá thì trả thời điểm hết khoá (UTC), không thì null.</summary>
    public static DateTime? LockedUntil(string empcd)
    {
        if (!_map.TryGetValue(Key(empcd), out var e)) return null;
        lock (e)
        {
            if (e.LockedUntilUtc is DateTime u && u > DateTime.UtcNow) return u;
            return null;
        }
    }

    /// <summary>Ghi 1 lần sai. Trả số lần còn được thử (0 = vừa bị khoá) và thời điểm hết khoá nếu có.</summary>
    public static (int remaining, DateTime? lockedUntilUtc) Fail(string empcd)
    {
        Cleanup();
        var now = DateTime.UtcNow;
        var e = _map.GetOrAdd(Key(empcd), _ => new Entry { FirstFailUtc = now });
        lock (e)
        {
            // Hết khoá hoặc lần sai đầu đã quá 15 phút → đếm lại từ đầu
            if ((e.LockedUntilUtc is DateTime u && u <= now) || now - e.FirstFailUtc > Window)
            {
                e.Fails = 0; e.FirstFailUtc = now; e.LockedUntilUtc = null;
            }
            e.Fails++;
            if (e.Fails >= MaxFails)
            {
                e.LockedUntilUtc = now + LockFor;
                return (0, e.LockedUntilUtc);
            }
            return (MaxFails - e.Fails, null);
        }
    }

    /// <summary>Đăng nhập đúng → xoá lượt đếm.</summary>
    public static void Success(string empcd) => _map.TryRemove(Key(empcd), out _);

    /// <summary>Câu báo khi đang bị khoá (giờ Việt Nam).</summary>
    public static string LockedMessage(DateTime lockedUntilUtc)
    {
        var vn = lockedUntilUtc.AddHours(7);
        return $"Bạn đã nhập sai {MaxFails} lần. Tài khoản tạm khoá đến {vn:HH:mm} (15 phút) để bảo vệ tài khoản. " +
               "Nếu quên mật khẩu, bấm \"Quên mật khẩu\" hoặc liên hệ phòng HR. / Too many failed attempts. " +
               $"Please try again after {vn:HH:mm}.";
    }

    private static void Cleanup()
    {
        var now = DateTime.UtcNow;
        if (now < _nextCleanUtc) return;
        _nextCleanUtc = now.AddMinutes(10);
        foreach (var kv in _map)
        {
            var e = kv.Value;
            bool stale;
            lock (e) stale = (e.LockedUntilUtc is DateTime u ? u <= now : true) && now - e.FirstFailUtc > Window;
            if (stale) _map.TryRemove(kv.Key, out _);
        }
    }
}
