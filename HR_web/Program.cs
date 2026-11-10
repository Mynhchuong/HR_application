using HR_web.Filters;
using HR_web.Helpers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.DataProtection;
using System.Globalization;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{

    options.Filters.Add<RequireUpdateProfileFilter>();
    options.Filters.Add<HR_web.Filters.SurveyBlockerFilter>();
    options.Filters.Add<HR_web.Filters.GiftGateFilter>();
}).AddNewtonsoftJson(options =>
{

    options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
});

// Các trang AJAX gửi JSON (vd Survey admin wizard) đính token qua header "RequestVerificationToken"
// thay vì form field — mặc định [ValidateAntiForgeryToken] chỉ tìm token trong Request.Form, không
// biết nhìn header nào nên luôn 400 trước khi action chạy (xác nhận thật qua SurveyAdmin/Save
// 2026-11-02). Khai báo đúng tên header JS đang dùng để server đọc được.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

builder.Services.AddMemoryCache();
    
// Persist keys ra thư mục CỐ ĐỊNH ngoài vùng deploy (yêu cầu 2026-11-10: trước đây lưu trong
// ContentRootPath/DataProtectionKeys — mỗi lần deploy (xoá sạch rồi copy code mới) thư mục này mất
// theo, toàn bộ user đang đăng nhập (kể cả app Android/iOS, vốn chỉ là WebView dùng chung cookie web)
// bị logout ngầm cùng lúc, kéo theo hệ quả phụ: token FCM chỉ đăng ký lại được lúc đang đăng nhập,
// nên nếu Firebase cấp token mới đúng lúc đang bị logout thì NV mất noti cho tới khi đăng nhập lại.
// C:\ProgramData không nằm trong physical path của site IIS nên không bao giờ bị xoá khi deploy —
// cần đảm bảo IIS App Pool Identity có quyền ghi vào thư mục này trên server trước khi deploy bản này.
// Chỉ áp dụng path cố định này khi chạy Production trên Windows/IIS — giữ nguyên hành vi cũ
// (ContentRootPath) lúc dev local trên máy Mac/Linux để không vỡ `dotnet run` hằng ngày.
var dataProtectionKeyPath = (!builder.Environment.IsDevelopment() && OperatingSystem.IsWindows())
    ? @"D:\SharedKeys\HR_Web\DataProtectionKeys"
    : Path.Combine(builder.Environment.ContentRootPath, "DataProtectionKeys");
builder.Services.AddDataProtection()
    .SetApplicationName("HR_Web")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));


builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = builder.Configuration["Auth:CookieName"] ?? ".HR.Auth";
        options.LoginPath = builder.Configuration["Auth:LoginPath"] ?? "/Account/Login";
        options.LogoutPath = builder.Configuration["Auth:LogoutPath"] ?? "/Account/Logout";
        // Hết hạn tuyệt đối 24h, KHÔNG sliding — bảo đảm mọi phiên đều bị ép logout
        // ít nhất 1 lần/ngày (AuthHelper.SignInAsync set ExpiresUtc = +24h mỗi lần đăng nhập).
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = false;
        options.Cookie.HttpOnly = true;
        options.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;
    });

builder.Services.AddHttpContextAccessor();

// ============================================================
// 4. HttpClient  
// ============================================================
builder.Services.AddHttpClient("SamhoAPI", client =>
{
    var baseUrl = builder.Configuration["ApiSettings:BaseUrl"]
                  ?? "http://192.168.1.24/HR_api/apiHR/";
    client.BaseAddress = new Uri(baseUrl);
    // 300s (không phải 60s) — chịu được các call bulk xử lý nhiều bản ghi trong 1 request
    // (VD Inquiry/bulk-close đóng hàng loạt hàng ngàn hội thoại), không ảnh hưởng các call
    // nhanh bình thường vì đây chỉ là giới hạn tối đa, không phải thời gian chờ cố định.
    client.Timeout = TimeSpan.FromSeconds(300);

    var apiKey = builder.Configuration["ApiSettings:ApiKey"] ?? "";
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

// ============================================================
// 5. Session (lưu TempData) 
// ============================================================
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ============================================================
// 6. Request size limit (thay maxRequestLength="102400" trong Web.config)
// ============================================================
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524_288_000; // 500MB
});
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = 524_288_000; // 500MB
});

// ============================================================
// 5. Register Application Services (DI)
// ===========================================================
builder.Services.AddScoped<HR_web.API.ApiService>();
builder.Services.AddScoped<HR_web.API.Service.AccountService>();
builder.Services.AddScoped<HR_web.API.Service.OtService>();
builder.Services.AddScoped<HR_web.API.Service.TrainingService>();
builder.Services.AddScoped<HR_web.API.Service.PayslipService>();
builder.Services.AddScoped<HR_web.API.Service.DropdownService>();
builder.Services.AddScoped<HR_web.API.Service.UserDeptService>();
builder.Services.AddScoped<HR_web.API.Service.GatePassService>();
builder.Services.AddScoped<HR_web.API.Service.PolicyService>();
builder.Services.AddScoped<HR_web.API.Service.BulletinService>();
builder.Services.AddScoped<HR_web.API.Service.LeaveService>();
builder.Services.AddScoped<HR_web.API.Service.CalendarService>();
builder.Services.AddScoped<HR_web.API.Service.AttendanceConfirmService>();
builder.Services.AddScoped<HR_web.API.Service.GiftService>();
builder.Services.AddScoped<HR_web.API.Service.GuideService>();
builder.Services.AddScoped<HR_web.API.Service.AppLinkService>();
builder.Services.AddScoped<HR_web.API.Service.KaizenService>();
builder.Services.AddScoped<HR_web.API.Service.MenuService>();
builder.Services.AddScoped<HR_web.API.Service.MealLockService>();
builder.Services.AddScoped<HR_web.API.Service.CanteenBreadService>();
builder.Services.AddScoped<HR_web.API.Service.EmployeeService>();
builder.Services.AddScoped<HR_web.API.Service.NotificationService>();
builder.Services.AddScoped<HR_web.API.Service.NotiTemplateService>();
builder.Services.AddScoped<HR_web.API.Service.InquiryService>();
builder.Services.AddScoped<HR_web.API.Service.AiCsrService>();
builder.Services.AddScoped<HR_web.API.Service.TpmService>(); // Báo máy hư (TPM) cho công nhân sản xuất
builder.Services.AddScoped<HR_web.API.Service.DirectoryService>();
builder.Services.AddScoped<HR_web.API.Service.HomeApiService>();
builder.Services.AddScoped<HR_web.API.Service.HomeAdminApiService>();
builder.Services.AddScoped<HR_web.API.Service.SurveyService>();
builder.Services.AddScoped<HR_web.API.Service.SurveyAdminService>();
builder.Services.AddScoped<HR_web.API.Service.SurveyReportService>();
builder.Services.AddScoped<HR_web.API.Service.SurveyExemptService>();
builder.Services.AddScoped<HR_web.Filters.SurveyBlockerFilter>();
builder.Services.AddScoped<HR_web.Filters.GiftGateFilter>();
builder.Services.AddSingleton<HR_web.API.Service.VideoFileService>();
builder.Services.AddHostedService<HR_web.Services.InquiryTempCleanupService>();

// ============================================================
var app = builder.Build();
// ============================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePages(ctx =>
{
    var code = ctx.HttpContext.Response.StatusCode;
    if (HR_web.Helpers.MobileHelper.IsMobileApp(ctx.HttpContext) && (code == 404 || code >= 500))
    {
        // Cùng chốt chặn với HomeController.Error() — nếu chính /Home/Index trả 404/500 (không qua
        // exception) thì nhánh này sẽ redirect về chính nó, lặp vô hạn (báo cáo 2026-10-02 "app load
        // liên tục"). Dùng chung cookie "MOBILE_ERR_RETRY" (15s) để chỉ redirect 1 lần.
        if (!ctx.HttpContext.Request.Cookies.ContainsKey("MOBILE_ERR_RETRY"))
        {
            ctx.HttpContext.Response.Cookies.Append("MOBILE_ERR_RETRY", "1", new CookieOptions { MaxAge = TimeSpan.FromSeconds(15) });
            var pathBase = ctx.HttpContext.Request.PathBase.Value ?? "";
            ctx.HttpContext.Response.Redirect(pathBase + "/Home/Index");
        }
    }
    return Task.CompletedTask;
});

// Localization vi-VN
var supportedCultures = new[] { new CultureInfo("vi-VN") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("vi-VN"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

// Trước đây KHÔNG set Cache-Control cho static files (chỉ có ETag) — mỗi lần vào trang đều phải hỏi
// lại server cho từng file CSS/JS/font dù không đổi (round-trip 304, không nặng nhưng cộng dồn ~15
// file lib/ mỗi trang vẫn tốn thời gian, rõ nhất ở lần tải đầu/app WebView cache rỗng — báo cáo
// 2026-10-02 "load chậm, chưa kịp hiện menu dưới lúc đầu"). File trong wwwroot/lib đã gắn version
// hash qua asp-append-version nên cache dài hạn (1 năm, immutable) an toàn tuyệt đối — đổi file là
// đổi hash, không bao giờ trả nhầm bản cũ.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.PhysicalPath?.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}") == true)
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        }
    }
});

app.UseRouting();

app.UseSession();

// Authentication phải đặt TRƯỚC Authorization ko sửa nha nha  
app.UseAuthentication();
app.UseAuthorization();

// "/Notification" (không có action) — app Android/iOS mở thẳng đường dẫn này khi nhân viên bấm vào thông báo đẩy.
// Route mặc định bên dưới có action mặc định là Login nên trước đây /Notification trả 404 -> màn hình TRẮNG
// (báo 2026-10-28). Phải đặt TRƯỚC route default.
app.MapControllerRoute(
    name: "notification-root",
    pattern: "Notification",
    defaults: new { controller = "Notification", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
