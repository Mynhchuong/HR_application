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
}).AddNewtonsoftJson(options =>
{

    options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
});

builder.Services.AddMemoryCache();
    
// Persist keys to filesystem to prevent logout on app restart/idle
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "DataProtectionKeys")));


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
    client.Timeout = TimeSpan.FromSeconds(60);

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
builder.Services.AddScoped<HR_web.API.Service.GuideService>();
builder.Services.AddScoped<HR_web.API.Service.MenuService>();
builder.Services.AddScoped<HR_web.API.Service.MealLockService>();
builder.Services.AddScoped<HR_web.API.Service.EmployeeService>();
builder.Services.AddScoped<HR_web.API.Service.NotificationService>();
builder.Services.AddScoped<HR_web.API.Service.NotiTemplateService>();
builder.Services.AddScoped<HR_web.API.Service.InquiryService>();
builder.Services.AddScoped<HR_web.API.Service.HomeApiService>();
builder.Services.AddScoped<HR_web.API.Service.HomeAdminApiService>();
builder.Services.AddScoped<HR_web.API.Service.SurveyService>();
builder.Services.AddScoped<HR_web.API.Service.SurveyAdminService>();
builder.Services.AddScoped<HR_web.API.Service.SurveyReportService>();
builder.Services.AddScoped<HR_web.API.Service.SurveyExemptService>();
builder.Services.AddScoped<HR_web.Filters.SurveyBlockerFilter>();
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
        var pathBase = ctx.HttpContext.Request.PathBase.Value ?? "";
        ctx.HttpContext.Response.Redirect(pathBase + "/Home/Index");
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

app.UseStaticFiles();

app.UseRouting();

app.UseSession();

// Authentication phải đặt TRƯỚC Authorization ko sửa nha nha  
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
