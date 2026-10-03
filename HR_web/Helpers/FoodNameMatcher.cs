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
                ? ClauseSimilarity(clausesA!, Clauses(fn))
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

    // So combo với combo: cùng số mệnh đề, mỗi mệnh đề ghép 1-1 (không cần đúng thứ tự) và
    // Similarity() >= 0.85 — tái dùng đúng logic "giống" của món đơn cho từng mệnh đề con, vì
    // mệnh đề combo thực chất cũng là tên 1 món. Trước đây chỉ SetEquals tuyệt đối nên lọt lỗi gõ
    // nằm TRONG combo (vd "Khổ hoa dồn kkho nước tương + ..." không khớp "... kho nước tương + ...").
    private static double ClauseSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count != b.Count) return 0;
        if (a.SetEquals(b)) return 1.0;

        var remaining = new List<string>(b);
        bool anyFuzzy = false;
        foreach (var clause in a)
        {
            int exactIdx = remaining.IndexOf(clause);
            if (exactIdx >= 0) { remaining.RemoveAt(exactIdx); continue; }

            int bestIdx = -1; double bestSim = 0;
            for (int i = 0; i < remaining.Count; i++)
            {
                double sim = Similarity(clause, remaining[i]);
                if (sim > bestSim) { bestSim = sim; bestIdx = i; }
            }
            if (bestIdx < 0 || bestSim < 0.85) return 0;
            remaining.RemoveAt(bestIdx);
            anyFuzzy = true;
        }
        return anyFuzzy ? 0.9 : 1.0;
    }

    // Chỉ coi là "giống" khi (áp dụng cho món đơn, không combo):
    //  - Cùng hệt bộ từ nhưng đảo thứ tự (VD "Thịt kho trứng" <-> "Trứng kho thịt") → 1.0
    //  - Cùng SỐ từ, mỗi từ khớp y hệt hoặc gõ sai 1-2 ký tự (VD "kkho"/"kho", "chên"/"chiên",
    //    "cnon"/"non", "thit"/"thịt") → 0.9. Trước đây so "bộ từ y hệt tuyệt đối" nên lọt hàng loạt
    //    lỗi gõ thật kiểu này (rà tay 2026-10-02) vì "kkho" bị coi là 1 từ hoàn toàn khác "kho".
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
        {
            if (sa.SetEquals(sb)) return 1.0;
            return WordsNearMatch(wa, wb) ? 0.9 : 0;
        }

        if (diff == 1)
            return sa.IsSubsetOf(sb) || sb.IsSubsetOf(sa) ? 0.85 : 0;

        return 0;
    }

    // Cùng số từ, ghép được 1-1 (không cần đúng thứ tự) sao cho mỗi cặp hoặc y hệt hoặc cách nhau
    // tối đa 2 ký tự (EditDistance) — bắt lỗi gõ thiếu/dư/sai 1 ký tự trong 1 từ của tên món.
    // Không dùng khi từ quá ngắn (<=2 ký tự) để tránh báo nhầm (vd "bò" <-> "bí" cách 1 ký tự nhưng
    // là 2 nguyên liệu khác hẳn nhau).
    private static bool WordsNearMatch(string[] wa, string[] wb)
    {
        if (wa.Length != wb.Length) return false;
        var remaining = new List<string>(wb);
        int fuzzyPairs = 0;
        foreach (var w in wa)
        {
            int exactIdx = remaining.IndexOf(w);
            if (exactIdx >= 0) { remaining.RemoveAt(exactIdx); continue; }

            int bestIdx = -1, bestDist = int.MaxValue;
            for (int i = 0; i < remaining.Count; i++)
            {
                if (w.Length <= 2 || remaining[i].Length <= 2) continue;
                int d = EditDistance(w, remaining[i]);
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            if (bestIdx < 0 || bestDist > 2) return false;
            remaining.RemoveAt(bestIdx);
            fuzzyPairs++;
        }
        return fuzzyPairs > 0; // ít nhất 1 từ phải lệch mới coi là "gần giống" (0 từ lệch = đã bắt ở nhánh SetEquals)
    }

    private static int EditDistance(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return d[a.Length, b.Length];
    }
}
