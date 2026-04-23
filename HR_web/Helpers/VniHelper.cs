using System.Collections.Generic;

namespace HR_web.Helpers
{
    public static class VniHelper
    {
        public static string VniToUnicode(string vniString)
        {
            if (string.IsNullOrEmpty(vniString)) return vniString;

            string result = vniString;

            // Bảng ánh xạ ưu tiên từ dài đến ngắn để tránh thay thế sai
            // Dựa trên dữ liệu thực tế từ database (Biến thể VNI-Windows/TQ)
            var map = new Dictionary<string, string> {
                // Cụm 3 ký tự (Ưu tiên cao nhất)
                { "öôù", "ướ" }, { "öôø", "ườ" }, { "öôû", "ưở" }, { "öôõ", "ưỡ" }, { "öôï", "ượ" },
                { "aâù", "ấ" }, { "aâø", "ầ" }, { "aâû", "ẩ" }, { "aâõ", "ẫ" }, { "aâï", "ậ" },
                { "aêù", "ắ" }, { "aêø", "ằ" }, { "aêû", "ẳ" }, { "aêõ", "ẵ" }, { "aêï", "ặ" },
                { "eâù", "ế" }, { "eâø", "ề" }, { "eâû", "ể" }, { "eâõ", "ễ" }, { "eâï", "ệ" },
                { "oâù", "ố" }, { "oâø", "ồ" }, { "oâû", "ổ" }, { "oâõ", "ỗ" }, { "oâï", "ộ" },
                { "ưù", "ứ" }, { "ưø", "ừ" }, { "ưû", "ử" }, { "ưõ", "ữ" }, { "ưï", "ự" },

                // Cụm 2 ký tự
                { "ieá", "iế" }, { "ieà", "iề" }, { "ieå", "iể" }, { "ieã", "iễ" }, { "ieä", "iệ" },
                { "uoá", "uố" }, { "uoà", "uồ" }, { "uoå", "uổ" }, { "uoã", "uỗ" }, { "uoä", "uộ" },
                { "eá", "ế" }, { "eà", "ề" }, { "eå", "ể" }, { "eã", "ễ" }, { "eä", "ệ" },
                { "oá", "ố" }, { "oà", "ồ" }, { "oå", "ổ" }, { "oã", "ỗ" }, { "oä", "ộ" },
                { "öù", "ứ" }, { "öø", "ừ" }, { "öû", "ử" }, { "öõ", "ữ" }, { "öï", "ự" },
                { "ôù", "ớ" }, { "ôø", "ờ" }, { "ôû", "ở" }, { "ôõ", "ỡ" }, { "ôï", "ợ" },
                { "aù", "á" }, { "aø", "à" }, { "aû", "ả" }, { "aõ", "ã" }, { "aï", "ạ" },
                { "eù", "é" }, { "eø", "è" }, { "eû", "ẻ" }, { "eõ", "ẽ" }, { "eï", "ẹ" },
                { "iù", "í" }, { "iø", "ì" }, { "iû", "ỉ" }, { "iõ", "ĩ" }, { "iï", "ị" },
                { "où", "ó" }, { "oø", "ò" }, { "oû", "ỏ" }, { "oõ", "õ" }, { "oï", "ọ" },
                { "uù", "ú" }, { "uø", "ù" }, { "uû", "ủ" }, { "uõ", "ũ" }, { "uï", "ụ" },
                { "yù", "ý" }, { "yø", "ỳ" }, { "yû", "ỷ" }, { "yõ", "ỹ" }, { "yï", "ỵ" },
                { "aâ", "â" }, { "aê", "ă" }, { "eâ", "ê" }, { "oâ", "ô" }, { "ư", "ư" },
                { "aë", "ặ" },

                // Ký tự đơn đặc biệt (Dùng trong biến thể VNI-TQ/Custom)
                { "Ñ", "Đ" }, { "ñ", "đ" }, { "ã", "ễ" }, { "ö", "ư" }, { "ô", "ơ" }, 
                { "ï", "ị" }, { "ò", "ị" }, { "ù", "ú" }, { "ø", "ờ" }
            };

            foreach (var k in map.Keys)
            {
                result = result.Replace(k, map[k]);
                result = result.Replace(k.ToUpper(), map[k].ToUpper());
            }

            return result;
        }
    }
}
