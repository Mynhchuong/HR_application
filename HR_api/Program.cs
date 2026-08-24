using HR_api.Data;
using HR_api.Middleware;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ContractResolver = new DefaultContractResolver();
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "HR API - Samho", Version = "v1" });
    c.CustomSchemaIds(type => type.FullName);

    // Thêm ô nhập API Key trên Swagger UI
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Nhập API Key vào đây"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            []
        }
    });
});

builder.Services.AddScoped<OracleService>();
builder.Services.AddScoped<HR_api.Helpers.NotificationHelper>();
builder.Services.AddScoped<HR_api.Services.NotificationService>();
builder.Services.AddScoped<HR_api.Services.BulletinService>();
builder.Services.AddHostedService<HR_api.HostedServices.BulletinLifecycleService>();

// ============================================================
// Home page — trang chủ tuỳ biến theo role
// ============================================================
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HR_api.Helpers.HomeAuditHelper>();
builder.Services.AddScoped<HR_api.Helpers.OtLogHelper>();
builder.Services.AddScoped<HR_api.Services.ShiftLookupService>();
builder.Services.AddScoped<HR_api.Services.HomeBirthdayService>();
builder.Services.AddScoped<HR_api.Services.HomeSummaryService>();
builder.Services.AddScoped<HR_api.Services.HomeService>();
builder.Services.AddScoped<HR_api.Services.HomeMyCalendarService>();
builder.Services.AddScoped<HR_api.Services.HomeAdminService>();

// ============================================================
// Survey — HR gửi khảo sát / trắc nghiệm cho NV
// ============================================================
builder.Services.AddScoped<HR_api.Services.SurveyAdminService>();
builder.Services.AddScoped<HR_api.Services.SurveyService>();
builder.Services.AddScoped<HR_api.Services.SurveyScopeService>();
builder.Services.AddScoped<HR_api.Services.SurveyRecipientService>();
builder.Services.AddScoped<HR_api.Services.SurveyScoreService>();
builder.Services.AddScoped<HR_api.Services.SurveyExemptService>();
builder.Services.AddScoped<HR_api.Services.SurveyReportService>();
builder.Services.AddHostedService<HR_api.HostedServices.SurveyLifecycleService>();

// ============================================================
// Training — Course/Class/Enrollment/Session/Material/Q&A/Test (phase 1 + 2 + 3)
// ============================================================
builder.Services.AddScoped<HR_api.Services.TrainingCourseService>();
builder.Services.AddScoped<HR_api.Services.TrainingClassService>();
builder.Services.AddScoped<HR_api.Services.TrainingEnrollmentService>();
builder.Services.AddScoped<HR_api.Services.TrainingSessionService>();
builder.Services.AddScoped<HR_api.Services.TrainingMaterialService>();
builder.Services.AddScoped<HR_api.Services.TrainingQAService>();
builder.Services.AddScoped<HR_api.Services.TrainingTestService>();
builder.Services.AddScoped<HR_api.Services.TrainingAttemptService>();
builder.Services.AddScoped<HR_api.Services.TrainingReviewService>();
builder.Services.AddScoped<HR_api.Services.TrainingCompletionService>();
builder.Services.AddScoped<HR_api.Services.TrainingReportService>();
builder.Services.AddScoped<HR_api.Services.TrainingNotificationService>();
builder.Services.AddScoped<HR_api.Services.TrainingTeamService>();
builder.Services.AddScoped<HR_api.Helpers.TrainingAuthHelper>();
builder.Services.AddHostedService<HR_api.HostedServices.TrainingLifecycleService>();
builder.Services.AddHostedService<HR_api.HostedServices.TrainingNotificationWorker>();

// Leave — nhắc nộp giấy tờ (Đám tang/Đám cưới/Vợ sanh/Khám thai)
builder.Services.AddHostedService<HR_api.HostedServices.LeaveDocReminderService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.WebHost.UseIISIntegration();

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 104_857_600; // 100MB
});


var app = builder.Build();

app.UseDeveloperExceptionPage();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("swagger/v1/swagger.json", "HR API v1");
    c.RoutePrefix = string.Empty;
});

app.UseStaticFiles();  // serve /uploads/menu/...

app.UseCors("AllowAll");

app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthorization();

app.MapGet("/check-db", async (OracleService db) =>
{
    try
    {
        await db.ExecuteQueryAsync("SELECT 1 FROM DUAL", r => 1);
        return Results.Ok(new { success = true, message = "Kết nối Oracle 10g THÀNH CÔNG!" });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { success = false, message = "LỖI KẾT NỐI DB: " + ex.Message, detail = ex.ToString() });
    }
});

app.MapControllers();

app.Run();
