using System.Globalization;
using System.Text;

namespace HR_api.Helpers;

/// <summary>
/// Gợi ý chủ đề (G01–G22) cho trang "Phân loại nhanh" của CSR — theo TỪ KHOÁ trong nội dung, không dùng AI
/// (đơn giản, giải thích được, miễn phí; CSR luôn là người chốt). Mức độ + phòng phụ trách + số ngày hạn lấy theo chủ đề đã cài ở DB,
/// nên sửa ở trang "Chủ đề hội thoại" là gợi ý đổi theo; chỉ danh sách từ khoá nằm ở file này.
/// Từ khoá viết KHÔNG DẤU, chữ thường; nội dung chat được bỏ dấu + chữ thường trước khi so khớp.
/// </summary>
public static class InquiryTopicSuggester
{
    public record Suggestion(string TopicCd, string Confidence, string Reason);

    // Thứ tự quan trọng khi bằng điểm: mục trước thắng (ZT/nguy cơ cao đặt trước).
    private static readonly (string Cd, string[] Words)[] Rules =
    {
        ("G08", new[] { "quay roi", "xam hai", "tinh duc", "so mo", "dung cham", "ep buoc quan he", "goi y tinh duc" }),
        ("G19", new[] { "binh dang gioi", "phan biet nam nu", "phan biet gioi", "phan biet doi xu gioi" }),
        ("G20", new[] { "doi xu khong cong bang", "bat cong", "thien vi", "phan biet doi xu", "chen ep", "bi mang", "bi chui", "hanh ha", "xuc pham", "bi doa" }),
        ("G15", new[] { "thai san", "mang thai", "co thai", "phu nu", "nuoi con", "con nho", "sinh con", "me bau", "bau bi" }),
        ("G06", new[] { "ky luat", "canh cao", "sa thai", "bien ban", "bi phat", "khien trach" }),
        ("G07", new[] { "thoi viec", "nghi viec", "cham dut hop dong", "chot so", "tra so", "so bhxh", "tro cap thoi viec", "xin nghi han" }),
        ("G04", new[] { "hop dong", "tuyen dung", "thu viec", "ky hop dong", "gia han hop dong", "tai ky" }),
        ("G02", new[] { "phep nam", "phep benh", "nghi phep", "xin phep", "ngay phep", "nghi benh", "giay nghi" }),
        ("G01", new[] { "luong", "bhxh", "bao hiem", "phu cap", "tien thuong", "thuong tet", "thue", "phieu luong", "tru tien", "thieu luong", "chua nhan luong", "tien com", "nang luong" }),
        ("G03", new[] { "gio lam", "ca lam", "tang ca", "ngay nghi", "nghi le", "cham cong", "ca dem", "ca 3", "lam them", "ot ", "gio nghi" }),
        ("G05", new[] { "noi quy", "quy dinh", "quy che", "dong phuc", "the ra vao", "di tre" }),
        ("G09", new[] { "cong doan" }),
        ("G11", new[] { "nuoc uong", "nuoc nong", "nuoc lanh", "may loc nuoc", "binh nuoc", "nuoc sach" }),
        ("G10", new[] { "ve sinh", "toilet", "nha ve sinh", "nha tam", "mui hoi", "ban", "rac", "con trung", "chuot", "gian" }),
        ("G13", new[] { "nha an", "bua an", "suat an", "com ", "thuc don", "canteen", "do an", "mon an", "an trua", "bun", "mon nhe", "bep an", "dau bep" }),
        ("G12", new[] { "an toan", "bao ho", "tai nan", "chay no", "hoa chat", "bui", "tieng on", "nong", "nhiet do", "khau trang", "gang tay", "sap nha" }),
        ("G17", new[] { "may lanh", "quat", "ghe ", "may moc", "thiet bi", "bai do xe", "bai xe", "den dien", "mai che", "sua chua", "xuong nuoc", "tu do", "locker" }),
        ("G14", new[] { "xe dua don", "xe buyt", "tuyen xe", "atm", "tham hoi", "quyen loi", "phuc loi", "tien xang", "chung chi" }),
        ("G16", new[] { "qua tai", "qua nhieu viec", "ep tang ca", "chi tieu cao", "kiet suc", "met moi" }),
        ("G21", new[] { "ky tuc xa", "ktx", "nha tro", "o tro", "cho o", "phong tro" }),
        ("G18", new[] { "de nghi", "yeu cau", "mong muon", "xin cong ty", "kien nghi", "de xuat" }),
    };

    // Chủ đề cũ công nhân đã chọn → chủ đề mới gần nhất (dùng khi nội dung không có từ khoá)
    private static readonly Dictionary<string, string> LegacyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PAYROLL"] = "G01", ["LEAVE"] = "G02", ["OT"] = "G03", ["MANAGEMENT"] = "G05", ["CANTEEN"] = "G13",
        ["CLEANNING"] = "G10", ["ANTOAN"] = "G12"
    };

    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        // bỏ thẻ HTML + dấu + chữ thường
        var noHtml = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", " ");
        var d = noHtml.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch == 'đ' || ch == 'Đ' ? 'd' : char.ToLowerInvariant(ch));
        }
        return " " + System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ") + " ";
    }

    public static Suggestion Suggest(string? text, string? currentTopicCd)
    {
        var t = Normalize(text);
        string? best = null; int bestScore = 0; string reason = "";
        foreach (var (cd, words) in Rules)
        {
            int score = 0; string first = "";
            foreach (var w in words)
            {
                var needle = w.EndsWith(' ') ? " " + w : w.Contains(' ') ? w : " " + w;
                if (t.Contains(needle)) { score++; if (first.Length == 0) first = w.Trim(); }
            }
            if (score > bestScore) { best = cd; bestScore = score; reason = $"có từ \"{first}\""; }
        }
        if (best != null) return new Suggestion(best, bestScore >= 2 ? "high" : "medium", reason);

        if (!string.IsNullOrEmpty(currentTopicCd))
        {
            if (currentTopicCd.StartsWith("G") && currentTopicCd.Length == 3) return new Suggestion(currentTopicCd, "medium", "giữ chủ đề công nhân đã chọn");
            if (LegacyMap.TryGetValue(currentTopicCd, out var m)) return new Suggestion(m, "medium", "theo chủ đề công nhân đã chọn");
        }
        return new Suggestion("G22", "low", "không nhận ra từ khoá — để CSR chọn");
    }
}
