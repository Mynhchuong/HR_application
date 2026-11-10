namespace HR_web.Helpers;

/// <summary>
/// Đánh giá hộp thư phản ánh: công nhân chấm 1–5 SAO (giữ thang cũ cho đồng bộ dữ liệu), báo cáo quy ra 3 mức (yêu cầu khách #10):
///   4–5 sao = Hài lòng · 3 sao = Bình thường · 1–2 sao = Không hài lòng (bắt buộc nêu lý do).
/// Chưa đánh giá (để trống) được tính là Hài lòng trong báo cáo (quy tắc của khách). DB không đổi: HR_INQUIRY.RATING 1..5.
/// </summary>
public static class InquiryRatingHelper
{
    public static string Key(int? r) => r == null || r <= 0 ? "" : r >= 4 ? "SATISFIED" : r == 3 ? "NORMAL" : "NOT_SATISFIED";
    public static string Label(int? r) => Key(r) switch { "SATISFIED" => "Hài lòng", "NORMAL" => "Bình thường", "NOT_SATISFIED" => "Không hài lòng", _ => "" };
    public static string Emoji(int? r) => Key(r) switch { "SATISFIED" => "😊", "NORMAL" => "😐", "NOT_SATISFIED" => "😞", _ => "" };
    public static string Color(int? r) => Key(r) switch { "SATISFIED" => "#16a34a", "NORMAL" => "#d97706", "NOT_SATISFIED" => "#dc2626", _ => "#64748b" };
}
