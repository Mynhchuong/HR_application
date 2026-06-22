using System.Text.RegularExpressions;

namespace HR_web.Helpers;

public static class ChatHtmlHelper
{
    private static readonly Regex ScriptRe = new(@"<script[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IframeRe = new(@"<iframe[\s\S]*?</iframe>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EventRe  = new(@"\son\w+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JsProtoRe = new(@"javascript\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = html;
        s = ScriptRe.Replace(s, "");
        s = IframeRe.Replace(s, "");
        s = EventRe.Replace(s, "");
        s = JsProtoRe.Replace(s, "");
        return s;
    }
}
