using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;

namespace HR_web.Filters;

/// <summary>
/// LỚP DỊCH THÔNG BÁO CHO EXPAT — đặt ở 1 chỗ, áp cho toàn bộ app.
///
/// Vì sao cần: thông báo lỗi/thành công phần lớn do HR_api sinh ra bằng tiếng Việt, mà API không biết
/// người gọi là ai nên không tự dịch được. Trước đây phải sửa tay từng chỗ (`isExpat ? "EN" : "VI"`),
/// vừa sót vừa chỉ ra được tiếng Anh, Expat người Hàn vẫn đọc tiếng Anh.
///
/// Cách chạy: sau khi action xong, nếu người đăng nhập là Expat thì filter soi kết quả trả về và
/// thay phần "message" bằng bản dịch theo UiLang (en/ko):
///   • JsonResult          — các endpoint AJAX (duyệt/từ chối đơn...)
///   • ContentResult json  — các endpoint proxy thẳng JSON của API
///   • TempData *Message   — thông báo hiện sau khi chuyển trang
///
/// Câu nào chưa có trong bảng ExpatText.ViToEn thì GIỮ NGUYÊN tiếng Việt — không bao giờ nuốt mất
/// thông báo. Muốn dịch thêm câu: thêm 1 dòng vào ViToEn + 1 dòng tiếng Hàn trong Ko, KHÔNG sửa ở đây.
///
/// Dùng IAlwaysRunResultFilter để chạy cả khi action bị filter khác chặn (xem AntiforgeryExpiredFilter).
/// </summary>
public class ExpatMessageFilter : IAlwaysRunResultFilter
{
    // Các khoá TempData đang dùng cho thông báo trong toàn app
    private static readonly string[] TempKeys = { "ErrorMessage", "SuccessMessage", "InfoMessage", "WarningMessage" };

    public void OnResultExecuting(ResultExecutingContext context)
    {
        // Chặn SỚM bằng claim Role (rẻ) trước khi gọi GetCurrentUser — hàm đó parse JSON cả object
        // UserInfoModel, mà filter này chạy ở MỌI request. Chỉ Expat mới phải trả cái giá đó.
        if (!context.HttpContext.User.IsInRole("Expat")) return;

        UserInfoModel? user;
        try { user = AuthHelper.GetCurrentUser(context.HttpContext.User); }
        catch { return; }

        if (user is null || !user.IsExpatUi) return;   // NV thường giữ nguyên tiếng Việt
        var lang = user.UiLang;

        try
        {
            foreach (var k in TempKeys)
                if (context.Controller is Controller c && c.TempData[k] is string sv && sv.Length > 0)
                    c.TempData[k] = ExpatText.Msg(lang, sv);

            switch (context.Result)
            {
                case JsonResult jr when jr.Value is not null:
                    // Dựng lại bằng JObject: app cấu hình Newtonsoft + DefaultContractResolver nên
                    // JObject serialize ra y hệt object gốc, không đổi tên thuộc tính (res.message bên JS).
                    // JToken.FromObject chứ không JObject.FromObject: nhiều endpoint trả thẳng mảng
                    // (List<...>), JObject.FromObject sẽ ném lỗi -> rơi vào catch và phí công vô ích.
                    if (JToken.FromObject(jr.Value) is JObject jo && Patch(jo, lang)) jr.Value = jo;
                    break;

                case ContentResult cr when cr.Content is { Length: > 0 }
                                        && (cr.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false):
                    if (cr.Content.TrimStart().StartsWith("{", StringComparison.Ordinal))
                    {
                        var co = JObject.Parse(cr.Content);
                        if (Patch(co, lang)) cr.Content = co.ToString(Newtonsoft.Json.Formatting.None);
                    }
                    break;
            }
        }
        catch { /* dịch lỗi thì trả nguyên bản, không được làm hỏng response */ }
    }

    // Đổi "message"/"Message" ở cấp ngoài cùng. Chỉ cấp ngoài cùng là đủ: đây là chỗ mọi endpoint
    // đặt thông báo cho người dùng; đi sâu hơn dễ đụng nhầm dữ liệu nghiệp vụ có field trùng tên.
    private static bool Patch(JObject o, string lang)
    {
        bool changed = false;
        foreach (var name in new[] { "message", "Message" })
            if (o[name]?.Type == JTokenType.String)
            {
                var vi = o[name]!.Value<string>() ?? "";
                var tr = ExpatText.Msg(lang, vi);
                if (tr != vi) { o[name] = tr; changed = true; }
            }
        return changed;
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
