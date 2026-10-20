using System.Text;

namespace HR_api.Tpm;

/// <summary>Đổi chuỗi bảng mã VNI (Windows) sang Unicode. Tên nhân viên trong HR (ECM100.CNAME) lưu dạng
/// VNI, vd "Nguyeãn Thò Thuùy Haèng" — HR_web hiển thị đúng nhờ font Vnitbi__ (class vni-font). TPM
/// là hệ thống khác, không có font đó, nên HR_api đổi sang Unicode trước khi gửi sang TPM.
/// Chuỗi ASCII thuần (vd tên bộ phận tiếng Anh) đi qua không đổi.</summary>
internal static class VniConverter
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

    public static string? ToUnicode(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // Nhanh: chuỗi thuần ASCII không cần đổi.
        var ascii = true;
        foreach (var c in s) { if (c > 127) { ascii = false; break; } }
        if (ascii) return s;

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
