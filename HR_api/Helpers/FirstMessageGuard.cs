using System.Globalization;
using System.Text;

namespace HR_api.Helpers;

/// <summary>
/// Chặn TIN NHẮN ĐẦU TIÊN của công nhân nếu nó chưa nói gì về vấn đề.
///
/// Lý do (CSR yêu cầu 2026-11-12): rất nhiều hội thoại mở ra chỉ có "hi admin", "chị ơi",
/// "em muốn hỏi", "123456"... CSR phải nhắn lại hỏi "bạn cần gì?" rồi chờ, mỗi ca mất hàng chục
/// lượt như vậy. Bắt gõ thẳng vấn đề ngay tin đầu để CSR trả lời được luôn.
///
/// CHỈ áp cho tin ĐẦU TIÊN và CHỈ với người gửi là công nhân — từ tin thứ hai trở đi nói gì
/// cũng được (chào hỏi, cảm ơn...), và HR/CSR không bị ràng buộc.
///
/// Nguyên tắc khi chỉnh: thà lọt một tin xấu còn hơn chặn nhầm một vấn đề thật. Công nhân bị
/// chặn oan sẽ bỏ luôn, không phản ánh nữa — hỏng cả kênh.
///
/// ĐÃ ĐO TRÊN DỮ LIỆU THẬT (8.862 tin nhắn đầu tiên trong HR_INQUIRY_MSG, 2026-11-12):
///   - Lời chào thuần ("hi", "hello", "Xin chào") chỉ có 3 ca — hiếm hơn nhiều so với cảm nhận.
///   - 788 tin là SỐ ĐIỆN THOẠI 10 số và nhiều tin là email: công nhân gửi theo yêu cầu cập nhật
///     BHXH của công ty. ĐÂY LÀ NỘI DUNG HỢP LỆ.
///   - Rất nhiều tin hợp lệ cực ngắn: "Email", "Đổi gmail", "Địa chỉ sai", "Cập nhật".
/// Bản đầu dùng ngưỡng 10 ký tự + chặn mọi dãy số đã chặn nhầm 980/8862 tin (11%). Vì vậy giờ
/// chỉ chặn thứ CHẮC CHẮN vô nghĩa. ĐỪNG siết ngưỡng lại nếu chưa đo lại trên dữ liệu thật.
/// </summary>
public static class FirstMessageGuard
{
    /// <summary>Lời mở đầu vô nghĩa — bỏ khỏi câu trước khi đo xem còn nội dung gì không.</summary>
    private static readonly string[] Openers =
    {
        "xin chao", "kinh chao", "chao buoi sang", "chao buoi chieu", "chao",
        "hello", "helo", "hallo", "hi", "hey", "alo", "a lo",
        "em muon hoi", "minh muon hoi", "toi muon hoi", "cho em hoi", "cho minh hoi",
        "cho toi hoi", "em hoi", "minh hoi", "em xin hoi", "em can hoi",
        "em co viec", "em co van de", "cho em hoi chut", "hoi chut",
        "admin", "ad", "anh chi", "anh", "chi", "em", "oi", "a", "c", "ac",
        "nhan su", "phong nhan su", "hr", "csr",
        "giup em", "giup minh", "lam phien", "xin phep", "da", "vang", "ok", "oke"
    };

    /// <summary>
    /// Số ký tự tối thiểu CÒN LẠI sau khi bỏ lời chào. Để RẤT THẤP là có chủ ý — xem
    /// ghi chú "đo trên dữ liệu thật" ở đầu class.
    /// </summary>
    private const int MinMeaningful = 3;

    /// <summary>
    /// null = cho gửi. Khác null = lý do chặn, hiển thị thẳng cho công nhân.
    /// </summary>
    public static string? Reject(string? content)
    {
        var raw = (content ?? "").Trim();
        if (raw.Length == 0) return null;   // rỗng đã có validate khác lo

        // Có email hoặc số điện thoại = đang cung cấp thông tin theo yêu cầu của HR -> cho qua ngay.
        if (raw.Contains('@') && raw.Contains('.')) return null;
        if (System.Text.RegularExpressions.Regex.IsMatch(raw, @"\d[\d .+()-]{7,}")) return null;

        var norm = Normalize(raw);

        // 1) Toàn số / ký tự lặp / bàn phím gõ bậy: "123456", "aaaa", "....", "..."
        var noSpace = norm.Replace(" ", "");
        if (noSpace.Length > 0)
        {
            // Dãy số NGẮN mới là gõ bậy. Số dài là SỐ ĐIỆN THOẠI — hợp lệ: đợt cập nhật BHXH
            // công ty YÊU CẦU công nhân nhắn số điện thoại/email vào đây (788/8862 tin đầu trong
            // lịch sử là SĐT 10 số). Chặn nhầm nhóm này là hỏng cả một đợt thu thập thông tin.
            if (noSpace.All(char.IsDigit) && noSpace.Length < 7)
                return "Tin nhắn đầu tiên cần mô tả vấn đề. Ví dụ: \"Tháng 10 em bị mất chuyên cần\".";
            // CHỈ chặn khi lặp đúng MỘT ký tự ("aaaa", "....", "xxxxx"). Dùng <= 2 sẽ chặn nhầm
            // từ viết tắt thật như "Cccd" (căn cước công dân) — gặp trong dữ liệu thật.
            if (noSpace.Length >= 3 && noSpace.Distinct().Count() == 1)
                return "Tin nhắn chưa rõ nội dung. Bạn hãy ghi thẳng vấn đề cần hỏi.";
        }

        // 2) Bỏ các lời mở đầu ở ĐẦU câu, lặp cho tới khi không bỏ được nữa
        //    ("chao anh chi, em muon hoi ..." -> bỏ 3 cụm liên tiếp).
        var rest = norm;
        bool stripped = true;
        while (stripped)
        {
            stripped = false;
            foreach (var w in Openers.OrderByDescending(x => x.Length))
            {
                if (rest == w) { rest = ""; stripped = true; break; }
                if (rest.StartsWith(w + " ", StringComparison.Ordinal))
                {
                    rest = rest[(w.Length + 1)..].TrimStart(' ', ',', '.', '!', '?', ':', ';');
                    stripped = true;
                    break;
                }
            }
        }

        // Chỉ chặn khi bỏ hết lời chào thì CHẲNG CÒN GÌ ("hi", "chào ad", "em muốn hỏi").
        // Còn vài chữ là cho qua, kể cả rất ngắn ("Email", "Đổi gmail", "Địa chỉ sai") — đó là
        // cách công nhân thật sự viết, đo trên 8862 tin đầu trong DB.
        if (rest.Replace(" ", "").Length < MinMeaningful)
            return "Bạn hãy nhắn thẳng vấn đề ngay tin đầu tiên (không cần chào hỏi) để được trả lời nhanh. "
                 + "Ví dụ: \"Tháng 10 em bị mất chuyên cần, em đã xin phép quản lý\".";

        return null;
    }

    /// <summary>Thường hoá + bỏ dấu tiếng Việt để so khớp ("Chào Chị" = "chao chi").</summary>
    private static string Normalize(string s)
    {
        var lower = s.ToLowerInvariant().Replace('đ', 'd');
        var noMark = new string(lower.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        // gộp mọi ký tự không phải chữ/số thành khoảng trắng, nén khoảng trắng
        var sb = new StringBuilder();
        foreach (var c in noMark) sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
