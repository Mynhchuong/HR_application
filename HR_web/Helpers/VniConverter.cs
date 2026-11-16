using System.Text;

namespace HR_web.Helpers;

/// <summary>
/// Đổi chuỗi bảng mã VNI (Windows) sang Unicode. Tên nhân viên trong ERP (ECM100.CNAME) lưu dạng VNI,
/// vd "Nguyeãn Minh Tröôøng". Giao diện WEB hiển thị đúng nhờ font Vnitbi__ (class vni-font trong
/// _Layout) nên KHÔNG được đổi ở đó — đổi sang Unicode rồi vẽ bằng font VNI thì lại sai chữ khác.
///
/// Chỗ cần đổi là những nơi KHÔNG có font đó: widget màn hình chính của app iOS/Android vẽ bằng
/// giao diện gốc nên tên hiện ra là "Nguyeãn Minh Tröôøng" (gặp thật trên tablet 2026-11-16).
/// Bản sao của HR_api/Tpm/VniConverter.cs (2 dự án không tham chiếu nhau), thêm bước LooksVni.
/// </summary>
public static class VniConverter
{
    // (Unicode thường, VNI). Chữ hoa suy ra tự động: VNI hoa = ToUpper toàn chuỗi (dấu cũng hoa: AÙ, OÂ...).
    private static readonly (string uni, string vni)[] Lower =
    {
        ("à","aø"),("á","aù"),("ả","aû"),("ã","aõ"),("ạ","aï"),
        ("ă","aê"),("ắ","aé"),("ằ","aè"),("ẳ","aú"),("ẵ","aü"),("ặ","aë"),
        ("â","aâ"),("ấ","aá"),("ầ","aà"),("ẩ","aå"),("ẫ","aã"),("ậ","aä"),
        ("è","eø"),("é","eù"),("ẻ","eû"),("ẽ","eõ"),("ẹ","eï"),
        ("ê","eâ"),("ế","eá"),("ề","eà"),("ể","eå"),("ễ","eã"),("ệ","eä"),
        ("ì","ì"),("í","í"),("ỉ","æ"),("ĩ","ó"),("ị","ò"),
        ("ò","oø"),("ó","où"),("ỏ","oû"),("õ","oõ"),("ọ","oï"),
        ("ô","oâ"),("ố","oá"),("ồ","oà"),("ổ","oå"),("ỗ","oã"),("ộ","oä"),
        ("ơ","ô"),("ớ","ôù"),("ờ","ôø"),("ở","ôû"),("ỡ","ôõ"),("ợ","ôï"),
        ("ù","uø"),("ú","uù"),("ủ","uû"),("ũ","uõ"),("ụ","uï"),
        ("ư","ö"),("ứ","öù"),("ừ","öø"),("ử","öû"),("ữ","öõ"),("ự","öï"),
        ("ỳ","yø"),("ý","yù"),("ỷ","yû"),("ỹ","yõ"),("ỵ","î"),
        ("đ","ñ"),
    };

    private static readonly Dictionary<string, string> Map = Build();

    private static Dictionary<string, string> Build()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (uni, vni) in Lower)
        {
            d[vni] = uni;
            d[vni.ToUpperInvariant()] = uni.ToUpperInvariant();
            // Phòng dữ liệu nhập lẫn kiểu: chữ cái đầu hoa + dấu thường ("Aùnh") -> vẫn ra chữ hoa ("Ánh").
            if (vni.Length == 2) d[char.ToUpperInvariant(vni[0]) + vni.Substring(1)] = uni.ToUpperInvariant();
        }
        return d;
    }

    // Ký tự CHỈ có trong VNI, không phải chữ cái tiếng Việt Unicode nào — thấy là chắc chắn VNI.
    private const string VniOnlyChars = "\u00f8\u00fb\u00fc\u00ef\u00eb\u00e4\u00e5\u00e6\u00f1\u00f6\u00ee"
                                      + "\u00d8\u00db\u00dc\u00cf\u00cb\u00c4\u00c5\u00c6\u00d1\u00d6\u00ce";

    /// <summary>
    /// Chuỗi này có đúng là VNI không? Phải hỏi trước khi đổi, vì vài ký tự VNI trùng mặt chữ với
    /// chữ Unicode hợp lệ: tên Unicode "Tô Minh Trí" mà đem đổi mù sẽ thành "Tơ Minh Trí".
    ///
    /// Hai dấu hiệu:
    ///   1. Có ký tự chỉ VNI mới có (ø, ö, ñ, æ...).
    ///   2. Có cặp "nguyên âm + dấu rời" kiểu VNI ("eã", "oâ", "aù"...). Tiếng Việt Unicode gõ đúng
    ///      hầu như không sinh ra cặp này vì dấu đã dính liền vào chữ.
    /// Ngược lại, hễ có ký tự ngoài Latin-1 (ă, đ, ơ, ư, ạ, ễ...) thì chắc chắn ĐÃ là Unicode -> bỏ qua.
    /// </summary>
    public static bool LooksVni(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;

        foreach (var c in s)
        {
            if (c > 0x00FF) return false;              // đã là Unicode tiếng Việt
            if (VniOnlyChars.IndexOf(c) >= 0) return true;
        }

        for (var i = 0; i + 1 < s.Length; i++)
        {
            var pair = s.Substring(i, 2);
            if (Map.ContainsKey(pair) && !Ambiguous.Contains(pair)) return true;
        }

        return false;
    }

    /// <summary>
    /// Ba cặp VÔ PHƯƠNG phân biệt, vì trùng y hệt chữ tiếng Việt Unicode có thật:
    ///   "oà" là ồ trong VNI, nhưng cũng là "oà" trong "Hoàng"
    ///   "oá" là ố trong VNI, nhưng cũng là "oá" trong "Hoá"
    ///   "oã" là ỗ trong VNI, nhưng cũng là "oã" trong một số cách viết
    /// Nên "Hoàng An" có thể là VNI của "Hồng An", mà cũng có thể là Unicode "Hoàng An" — đoán kiểu
    /// gì cũng có người bị sai tên. Do đó KHÔNG lấy riêng mấy cặp này làm bằng chứng: phải có thêm
    /// dấu hiệu khác trong chuỗi thì mới kết luận là VNI (tên 3-4 chữ hầu như luôn có).
    /// Không có bằng chứng nào khác thì để nguyên — giữ y như hiện tại, không làm hỏng thêm.
    /// </summary>
    private static readonly HashSet<string> Ambiguous = new(StringComparer.Ordinal)
    {
        "oà", "oá", "oã", "Oà", "Oá", "Oã", "OÀ", "OÁ", "OÃ",
    };

    public static string? ToUnicode(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // Nhanh: chuỗi thuần ASCII không cần đổi.
        var ascii = true;
        foreach (var c in s) { if (c > 127) { ascii = false; break; } }
        if (ascii) return s;
        if (!LooksVni(s)) return s;                    // đã là Unicode -> đổi nữa là hỏng

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            // Ưu tiên cặp 2 ký tự (chữ + dấu) trước, rồi mới tới ký tự đơn (ơ, ư, ì, í, ĩ, ị, đ...).
            if (i + 1 < s.Length && Map.TryGetValue(s.Substring(i, 2), out var two)) { sb.Append(two); i++; }
            else if (Map.TryGetValue(s[i].ToString(), out var one)) sb.Append(one);
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
