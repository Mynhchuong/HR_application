using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Helpers;

public static class ChatHtmlHelper
{
    // ── Lọc HTML tin nhắn hộp thư — VIẾT LẠI 2026-11-21 (bảo mật) ─────────────────────────────────────
    // Bản cũ "xoá cái xấu" bằng vài regex (script/iframe/on...=/javascript:) → lách dễ: <svg/onload=...>
    // (không có khoảng trắng trước "on"), "jav&#x61;script:"... Công nhân nào cũng gửi được tin, nên mã
    // chèn vào sẽ chạy trong phiên của HR/CSR khi họ mở tin.
    //
    // Bản mới làm NGƯỢC LẠI: mọi thứ bị mã hoá thành chữ thường, chỉ thẻ trong danh sách cho phép mới được
    // DỰNG LẠI TỪ ĐẦU (tên thẻ + đúng các thuộc tính cho phép, giá trị đã kiểm tra rồi mã hoá). Thẻ lạ bị bỏ
    // nhưng giữ chữ bên trong. Danh sách lấy từ dữ liệu thật (2.871 tin có HTML dùng đúng: p span a br img
    // strong li mark ul ol em u s). Bản JS cùng luật: safeHtml() trong wwwroot/js/inquiry-chat.js.
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "span", "a", "img", "strong", "b", "em", "i", "u", "s", "mark",
        "ul", "ol", "li", "blockquote", "code", "pre", "h1", "h2", "h3", "h4", "hr"
    };
    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase) { "br", "img", "hr" };

    private const RegexOptions RO = RegexOptions.IgnoreCase | RegexOptions.Compiled;
    // Khối bỏ hẳn cả nội dung bên trong (nội dung của chúng không phải chữ để đọc)
    private static readonly Regex DropBlockRe = new(@"<(script|style|iframe|object|embed|template|noscript|textarea|title|svg|math|select)\b[\s\S]*?</\1\s*>", RO);
    private static readonly Regex TagChunkRe  = new(@"<[^<>]*>", RegexOptions.Compiled);
    private static readonly Regex TagParseRe  = new(@"^<\s*(/?)\s*([a-zA-Z][a-zA-Z0-9]*)((?:\s+[a-zA-Z_:][-a-zA-Z0-9_:.]*(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s""'=<>`]+))?)*)\s*/?\s*>$", RegexOptions.Compiled);
    private static readonly Regex AttrRe      = new(@"([a-zA-Z_:][-a-zA-Z0-9_:.]*)(?:\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'=<>`]+)))?", RegexOptions.Compiled);
    private static readonly Regex EntityAtRe  = new(@"\G&(#[0-9]{1,7}|#[xX][0-9a-fA-F]{1,6}|[a-zA-Z][a-zA-Z0-9]{1,31});", RegexOptions.Compiled);
    private static readonly Regex CssColorRe  = new(@"^(#[0-9a-fA-F]{3,8}|rgba?\(\s*[0-9.%\s,]+\)|[a-zA-Z]{3,20})$", RegexOptions.Compiled);

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = DropBlockRe.Replace(html, "");
        var sb = new StringBuilder(s.Length + 32);
        int last = 0;
        foreach (Match m in TagChunkRe.Matches(s))
        {
            sb.Append(EncodeText(s[last..m.Index]));
            var rebuilt = RebuildTag(m.Value);
            sb.Append(rebuilt ?? EncodeText(m.Value));   // null = không phải thẻ (vd "<3 >") → hiện như chữ
            last = m.Index + m.Length;
        }
        sb.Append(EncodeText(s[last..]));
        return sb.ToString();
    }

    /// <summary>Mã hoá chữ thường nhưng giữ nguyên entity hợp lệ (&amp;nbsp; &amp;amp;...) để không mã hoá 2 lần.</summary>
    private static string EncodeText(string t)
    {
        if (t.Length == 0) return t;
        var sb = new StringBuilder(t.Length + 16);
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            switch (c)
            {
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                case '&':
                    var m = EntityAtRe.Match(t, i);
                    if (m.Success) { sb.Append(m.Value); i += m.Length - 1; }
                    else sb.Append("&amp;");
                    break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>null = chuỗi không phải thẻ; "" = thẻ không cho phép (bỏ); còn lại = thẻ đã dựng lại sạch.</summary>
    private static string? RebuildTag(string chunk)
    {
        if (chunk.StartsWith("<!") || chunk.StartsWith("<?")) return "";   // comment / doctype
        var m = TagParseRe.Match(chunk);
        if (!m.Success) return null;
        var name = m.Groups[2].Value.ToLowerInvariant();
        if (!AllowedTags.Contains(name)) return "";
        if (m.Groups[1].Value == "/") return VoidTags.Contains(name) ? "" : $"</{name}>";

        var sb = new StringBuilder("<").Append(name);
        bool hasHref = false;
        foreach (Match a in AttrRe.Matches(m.Groups[3].Value))
        {
            var an = a.Groups[1].Value.ToLowerInvariant();
            var raw = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value;
            var val = WebUtility.HtmlDecode(raw ?? "");
            string? ok = (name, an) switch
            {
                ("a", "href")                          => SafeUrl(val, imageOnly: false),
                ("img", "src")                         => SafeUrl(val, imageOnly: true),
                ("img", "alt") or ("img", "filename")  => val,
                ("img", "width") or ("img", "height")  => val.All(char.IsDigit) && val.Length is > 0 and < 6 ? val : null,
                ("ol", "start")                        => val.All(char.IsDigit) && val.Length is > 0 and < 6 ? val : null,
                (_, "style")                           => SafeStyle(val),
                ("mark", "data-color")                 => CssColorRe.IsMatch(val.Trim()) ? val.Trim() : null,
                _                                      => null
            };
            if (ok == null) continue;
            if (an == "href") hasHref = true;
            sb.Append(' ').Append(an).Append("=\"").Append(WebUtility.HtmlEncode(ok)).Append('"');
        }
        // Link luôn mở tab mới, không cho trang đích điều khiển ngược lại trang chat
        if (name == "a" && hasHref) sb.Append(" target=\"_blank\" rel=\"noopener noreferrer nofollow\"");
        if (name == "img" && !sb.ToString().Contains(" src=")) return "";   // ảnh không có nguồn hợp lệ → bỏ
        return sb.Append('>').ToString();
    }

    // <img> chỉ được trỏ về ảnh CÙNG app (đường dẫn bắt đầu bằng "/", không phải "//") — giữ đúng luật cũ:
    // ảnh ngoài sẽ tự gọi ra domain lạ khi ai đó mở tin (tracking/lộ IP).
    // Link: http/https/mailto/tel hoặc đường dẫn tương đối. Bỏ ký tự điều khiển/khoảng trắng trước khi xét
    // vì trình duyệt cũng bỏ qua chúng ("java\tscript:" vẫn là javascript:).
    private static string? SafeUrl(string v, bool imageOnly)
    {
        var c = new string(v.Where(ch => !char.IsControl(ch) && !char.IsWhiteSpace(ch)).ToArray());
        if (c.Length == 0) return null;
        if (imageOnly)
            return c.StartsWith('/') && !c.StartsWith("//") && !c.StartsWith("/\\") ? c : null;
        int colon = c.IndexOf(':');
        int stop  = c.IndexOfAny(new[] { '/', '?', '#' });
        if (colon < 0 || (stop >= 0 && stop < colon)) return c;          // tương đối
        var scheme = c[..colon].ToLowerInvariant();
        return scheme is "http" or "https" or "mailto" or "tel" ? c : null;
    }

    // Chỉ giữ màu chữ / màu nền / canh lề — đủ cho trình soạn tin, chặn mọi CSS khác (url(), position...).
    private static string? SafeStyle(string v)
    {
        var parts = new List<string>();
        foreach (var decl in v.Split(';'))
        {
            int i = decl.IndexOf(':');
            if (i <= 0) continue;
            var prop = decl[..i].Trim().ToLowerInvariant();
            var val  = decl[(i + 1)..].Trim();
            if ((prop is "color" or "background-color") && CssColorRe.IsMatch(val)) parts.Add($"{prop}: {val}");
            else if (prop == "text-align" && val.ToLowerInvariant() is "left" or "right" or "center" or "justify") parts.Add($"text-align: {val.ToLowerInvariant()}");
        }
        return parts.Count > 0 ? string.Join("; ", parts) : null;
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
