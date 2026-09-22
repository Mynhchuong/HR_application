using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Helpers;

public static class ChatHtmlHelper
{
    private static readonly Regex ScriptRe = new(@"<script[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IframeRe = new(@"<iframe[\s\S]*?</iframe>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EventRe  = new(@"\son\w+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JsProtoRe = new(@"javascript\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ImgTagRe  = new(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ImgSrcRe  = new(@"src\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = html;
        s = ScriptRe.Replace(s, "");
        s = IframeRe.Replace(s, "");
        s = EventRe.Replace(s, "");
        s = JsProtoRe.Replace(s, "");
        // <img> chỉ được phép trỏ về ảnh CÙNG app (đường dẫn tương đối bắt đầu bằng "/", không phải
        // "//") — VD ảnh chèn từ Câu trả lời mẫu (ImageController.GetBulletinImage). Thêm Tiptap
        // Image extension (2026-09-22, sửa bug ảnh mất khi chèn Canned Reply vào chat) vô tình mở
        // đường cho <img src="http://domain-lạ/..."> sống sót qua Sanitize → mọi người mở tin nhắn
        // đều tự động gọi tới domain đó (tracking pixel/rò IP) — chặn thẳng ở đây, không dựa vào
        // allowBase64:false/validate phía client vì có thể bị bỏ qua khi gọi thẳng API gửi tin.
        s = ImgTagRe.Replace(s, m =>
        {
            var srcMatch = ImgSrcRe.Match(m.Value);
            string src = srcMatch.Success ? (srcMatch.Groups[2].Success ? srcMatch.Groups[2].Value : srcMatch.Groups[3].Value) : "";
            bool isSafeRelative = src.StartsWith("/") && !src.StartsWith("//");
            return isSafeRelative ? m.Value : "";
        });
        return s;
    }

    // Nguồn dữ liệu duy nhất cho icon/nhãn/link theo RefType — dùng chung cho mọi view chat
    // (Admin/Hr/Employee Inquiry) thay vì mỗi view tự lặp lại ternary riêng. Thêm loại trích dẫn
    // mới (vd tương lai) chỉ cần sửa 2 method này, không phải sửa từng view.
    public static (string Icon, string Label) RefTypeMeta(string? refType) => refType switch
    {
        "POLICY"   => ("gavel", "Quy định"),
        "GUIDE"    => ("play_circle", "Hướng dẫn"),
        "BULLETIN" => ("campaign", "Bản tin"),
        _          => ("link", refType ?? "")
    };

    public static string RefTypeUrl(IUrlHelper url, string? refType, long refId) => refType switch
    {
        "POLICY"   => url.Action("Detail", "Policy",   new { ids = refId }) ?? "#",
        "GUIDE"    => url.Action("Detail", "Guide",    new { id  = refId }) ?? "#",
        "BULLETIN" => url.Action("Detail", "Bulletin", new { id  = refId }) ?? "#",
        _          => "#"
    };
}
