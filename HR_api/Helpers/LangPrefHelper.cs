using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using System.Globalization;
using System.Text;

namespace HR_api.Helpers;

/// <summary>
/// Ngôn ngữ giao diện của NV nước ngoài (role Expat) — yêu cầu 2026-11-11.
/// Mặc định suy từ HRMS.ECM100.NATION lúc đăng nhập: Hàn Quốc -> "ko", quốc tịch khác -> "en".
/// NV tự bấm đổi thì lưu vào HRMS.HR_USERS.LANG_PREF và giá trị đó ĐÈ lên mặc định theo quốc tịch.
/// Role khác Expat luôn "vi" — không dùng helper này.
/// An toàn khi DB chưa chạy ALTER (chưa có cột): mọi hàm coi như "chưa từng đổi tay", chỉ dùng mặc định.
/// </summary>
public static class LangPrefHelper
{
    public const string Vi = "vi";
    public const string En = "en";
    public const string Ko = "ko";

    private static volatile bool _available;
    private static DateTime _nextProbeUtc = DateTime.MinValue;
    private static readonly TimeSpan ProbeEvery = TimeSpan.FromSeconds(60);
    private static readonly object ProbeLock = new();

    public static async Task<bool> IsAvailableAsync(OracleService db)
    {
        if (_available) return true;
        if (DateTime.UtcNow < _nextProbeUtc) return false;

        bool ok;
        try
        {
            var rows = await db.ExecuteQueryAsync(
                @"SELECT COUNT(*) AS C FROM ALL_TAB_COLUMNS
                   WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_USERS' AND COLUMN_NAME = 'LANG_PREF'",
                r => Convert.ToInt32(r["C"]));
            ok = rows.FirstOrDefault() > 0;
        }
        catch { ok = false; }

        lock (ProbeLock)
        {
            _available = ok;
            _nextProbeUtc = ok ? DateTime.MaxValue : DateTime.UtcNow + ProbeEvery;
        }
        return ok;
    }

    /// <summary>Lựa chọn NV đã tự đổi (null = chưa từng đổi, cứ theo quốc tịch).</summary>
    public static async Task<string?> GetAsync(OracleService db, string? empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd) || !await IsAvailableAsync(db)) return null;
        var rows = await db.ExecuteQueryAsync(
            "SELECT LANG_PREF FROM HRMS.HR_USERS WHERE EMPCD = :E AND ROWNUM = 1",
            // reader["X"]?.ToString() KHÔNG bắt được DBNull (trả "" chứ không phải null) — phải so sánh tường minh,
            // nếu không thì "chưa từng đổi" sẽ biến thành chuỗi rỗng và lọt qua check IsNullOrWhiteSpace ở chỗ gọi.
            r => r["LANG_PREF"] == DBNull.Value ? null : r["LANG_PREF"].ToString(),
            new OracleParameter("E", empcd.Trim()));
        var v = rows.FirstOrDefault();
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim().ToLowerInvariant();
    }

    /// <summary>Lưu lựa chọn NV tự đổi. false nếu DB chưa có cột, mã ngôn ngữ lạ, hoặc không có NV này.</summary>
    public static async Task<bool> SetAsync(OracleService db, string? empcd, string? lang)
    {
        if (string.IsNullOrWhiteSpace(empcd) || !IsSupported(lang) || !await IsAvailableAsync(db)) return false;
        var n = await db.ExecuteNonQueryAsync(
            "UPDATE HRMS.HR_USERS SET LANG_PREF = :L WHERE EMPCD = :E",
            new OracleParameter("L", lang!.Trim().ToLowerInvariant()),
            new OracleParameter("E", empcd.Trim()));
        return n > 0;
    }

    public static bool IsSupported(string? lang)
        => lang is Vi or En or Ko;

    /// <summary>
    /// Suy ngôn ngữ mặc định từ ECM100.NATION. Cột này là text tự do do HR gõ, dữ liệu thật rất lộn xộn:
    /// "HAØN QUOAC", "Haøn Quoac", "Haøn Quoác", "HANQUOC", "HAN QUOC" (font VNI lỗi) — nên phải bỏ dấu/bỏ
    /// khoảng trắng rồi so chuỗi con, KHÔNG so bằng tuyệt đối. Không khớp Hàn Quốc -> tiếng Anh.
    /// </summary>
    public static string DetectFromNation(string? nation)
    {
        if (string.IsNullOrWhiteSpace(nation)) return En;

        var s = nation.Trim().ToUpperInvariant();

        // Viết bằng chữ Hàn thì khớp thẳng, khỏi qua bước bỏ dấu bên dưới.
        if (s.Contains("한국") || s.Contains("대한민국")) return Ko;

        // Bỏ dấu rồi CHỈ giữ A-Z thuần ASCII. Hai bước, vì có 2 kiểu dữ liệu lẫn lộn trong cột này:
        //   - Unicode chuẩn ("HÀN QUỐC"): NFD tách À -> A + dấu, bỏ dấu đi thì còn "HANQUOC".
        //     Bỏ bước này thì À/Ố bị lọc mất luôn và ra "HNQUC" -> KHÔNG khớp (bug: người Hàn bị
        //     rơi về tiếng Anh nếu HR nhập lại bằng Unicode chuẩn — phát hiện 2026-11-12).
        //   - VNI lỗi font ("HAØN QUOAC"): Ø không tách được bằng NFD nên vẫn bị lọc như trước,
        //     ra "HANQUOAC" — giữ nguyên hành vi cũ, không phá dữ liệu đang có.
        // Đ/đ cũng không tách được bằng NFD nên đổi tay sang D trước (vd "ÑAØI LOAN").
        var norm = s.Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var compact = new string(norm
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Where(c => c >= 'A' && c <= 'Z')
            .ToArray());

        // Các biến thể thật đang có trong DB + cách viết tiếng Anh/Hàn thường gặp.
        string[] koreaKeys = { "HANQUOC", "HANQUAC", "HANQUOAC", "KOREA", "HANQUK", "KOR" };
        foreach (var k in koreaKeys)
            if (compact.Contains(k)) return Ko;

        // VNI lỗi font: "Ø" thay cho "À" trong "HAØN" -> sau khi lọc chữ cái vẫn còn "HAN...",
        // bắt thêm trường hợp chỉ cần có "HAN" đứng đầu + "QUO" ở sau (phòng biến thể chưa gặp).
        if (compact.StartsWith("HAN") && compact.Contains("QUO")) return Ko;

        return En;
    }
}
