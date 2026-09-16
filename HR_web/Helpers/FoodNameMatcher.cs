using System.Text.RegularExpressions;
using HR_web.Models.Menu;

namespace HR_web.Helpers;

public static class FoodNameMatcher
{
    // Ký tự phân cách món combo (nhiều món ghép trong 1 tên, VD "Gà kho khoai tây + chả bắp chiên").
    // Rất phổ biến trong danh mục thật (nhiều ngày ăn combo ghép 2-3 món) — combo KHÔNG được so với
    // món đơn (tên 1 món đơn nằm lọt trong combo không có nghĩa là trùng món — đã test DB thật: bỏ
    // combo-vs-đơn làm số cặp bị flag giảm từ 262 xuống 43), nhưng combo VẪN phải so được với combo
    // khác — vì công ty hay đổi giữa "+"/"," hoặc đổi thứ tự món con mà thực chất là 1 combo y hệt
    // (VD thật trong DB: "Bún bò giò heo + TRÁI CÂY" và "Bún bò giò heo, trái cây" là cùng 1 combo).
    private static readonly char[] ComboSeparators = { '+', ',', '/' };

    public static string Normalize(string s)
        => Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

    public static MenuFoodModel? FindSimilar(string name, string? foodType, IEnumerable<MenuFoodModel> candidates, int? excludeId = null)
    {
        var norm = Normalize(name);
        if (string.IsNullOrEmpty(norm)) return null;

        bool comboA = IsCombo(norm);
        HashSet<string>? clausesA = comboA ? Clauses(norm) : null;

        MenuFoodModel? best = null;
        double bestScore = 0;

        foreach (var f in candidates)
        {
            if (excludeId.HasValue && f.ID == excludeId.Value) continue;
            if (foodType != null && !string.Equals(f.FOOD_TYPE, foodType, StringComparison.OrdinalIgnoreCase)) continue;

            var fn = Normalize(f.FOOD_NAME);
            if (fn == norm) continue; // trùng tuyệt đối xử lý riêng ở chỗ gọi

            bool comboB = IsCombo(fn);
            if (comboA != comboB) continue; // combo chỉ so với combo, món đơn chỉ so với món đơn

            double score = comboA
                ? (clausesA!.SetEquals(Clauses(fn)) ? 1.0 : 0)
                : Similarity(norm, fn);

            if (score > bestScore)
            {
                bestScore = score;
                best = f;
            }
        }

        return bestScore > 0 ? best : null;
    }

    private static bool IsCombo(string normalized) => normalized.IndexOfAny(ComboSeparators) >= 0;

    // Tách combo thành từng món con, chuẩn hoá — dùng so sánh không phân biệt thứ tự/dấu phân cách.
    private static HashSet<string> Clauses(string normalized)
        => normalized.Split(ComboSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(c => Normalize(c))
            .Where(c => c.Length > 0)
            .ToHashSet();

    // Chỉ coi là "giống" khi (áp dụng cho món đơn, không combo):
    //  - Cùng hệt bộ từ nhưng đảo thứ tự (VD "Thịt kho trứng" <-> "Trứng kho thịt") → 1.0
    //  - Chênh đúng 1 từ và bộ từ ngắn nằm trọn trong bộ từ dài (thêm 1 từ bổ nghĩa,
    //    VD "Bún riêu" -> "Bún riêu cua") → 0.85
    // Chênh từ 2 từ trở lên → coi là món khác hẳn, không flag (tránh rác).
    private static double Similarity(string a, string b)
    {
        var wa = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sa = wa.ToHashSet();
        var sb = wb.ToHashSet();

        int diff = Math.Abs(wa.Length - wb.Length);
        if (diff == 0)
            return sa.SetEquals(sb) ? 1.0 : 0;

        if (diff == 1)
            return sa.IsSubsetOf(sb) || sb.IsSubsetOf(sa) ? 0.85 : 0;

        return 0;
    }
}
