using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Helpers;

public static class BulletinContentHelper
{
    // Link "Nhấn vào đây để đăng ký ngay" của bản tin lớp học từng được ghi cứng theo host lúc HR bấm đăng
    // (vd http://192.168.1.24/HR_Web/Training/ClassRegister/111). App mobile chạy bằng host khác
    // (sa1-hanaro.esamho.com / hr.esamho.com) nên bấm vào sẽ sang host lạ: không có cookie đăng nhập hoặc không
    // truy cập được -> trang tải mãi. Khi hiển thị, đổi mọi link kiểu này về đường dẫn tương đối của chính site.
    private static readonly Regex ClassRegisterLink = new(
        @"href\s*=\s*([""'])https?://[^/""']+(?:/[^""']*?)?/Training/ClassRegister/(\d+)[^""']*\1",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? FixClassRegisterLinks(string? html, IUrlHelper url)
    {
        if (string.IsNullOrEmpty(html)) return html;
        return ClassRegisterLink.Replace(html, m =>
        {
            var path = url.Action("ClassRegister", "Training", new { id = m.Groups[2].Value });
            return $"href={m.Groups[1].Value}{path}{m.Groups[1].Value}";
        });
    }
}
