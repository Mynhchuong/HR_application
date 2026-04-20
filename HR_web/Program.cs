using HR_web.Filters;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using System.Globalization;


var builder = WebApplication.CreateBuilder(args);

// ============================================================
// 1. MVC + Views
// ============================================================
builder.Services.AddControllersWithViews(options =>
{
    // Global filter: thay RequireUpdateProfileAttribute cũ
    options.Filters.Add<RequireUpdateProfileFilter>();
}).AddNewtonsoftJson(options =>
{
    // Giữ nguyên PascalCase cho JSON để tương thích với JS cũ
    options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
});

// ============================================================
// 2. Cookie Authentication (thay FormsAuthentication)
// ============================================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = builder.Configuration["Auth:CookieName"] ?? ".HR.Auth";
        options.LoginPath = builder.Configuration["Auth:LoginPath"] ?? "/Account/Login";
        options.LogoutPath = builder.Configuration["Auth:LogoutPath"] ?? "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(
            int.TryParse(builder.Configuration["Auth:ExpireDays"], out var days) ? days : 1
        );
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
    });

// ============================================================
// 3. HttpContextAccessor (để đọc user trong helper/service)
// ============================================================
builder.Services.AddHttpContextAccessor();

// ============================================================
// 4. HttpClient (thay static HttpClient trong ApiService cũ)
// ============================================================
builder.Services.AddHttpClient("SamhoAPI", client =>
{
    var baseUrl = builder.Configuration["ApiSettings:BaseUrl"]
                  ?? "http://192.168.1.24/HR_api/apiHR/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ============================================================
// 5. Session (lưu TempData)
// ============================================================
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ============================================================
// 6. Request size limit (thay maxRequestLength="102400" trong Web.config)
// ============================================================
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 104_857_600; // 100MB
});
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = 104_857_600; // 100MB
});

// ============================================================
// 5. Register Application Services (DI)
// ============================================================
builder.Services.AddScoped<HR_web.API.ApiService>();
builder.Services.AddScoped<HR_web.API.Service.AccountService>();
builder.Services.AddScoped<HR_web.API.Service.OtService>();
builder.Services.AddScoped<HR_web.API.Service.PayslipService>();
builder.Services.AddScoped<HR_web.API.Service.DropdownService>();

// ============================================================
var app = builder.Build();
// ============================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

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

// Authentication phải đặt TRƯỚC Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
