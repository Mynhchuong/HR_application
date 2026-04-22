using HR_api.Data;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// 1. SERVICES
// ============================================================
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        // Maintain PascalCase for JSON property names to match legacy SamhoAPI/JavaScript
        options.SerializerSettings.ContractResolver = new DefaultContractResolver();
    });

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "HR API - Samho", Version = "v1" });
});

// Register Oracle Service (Scoped to ensure connection per request)
builder.Services.AddScoped<OracleService>();
builder.Services.AddScoped<HR_api.Helpers.NotificationHelper>();

// CORS (Allow access from mobile/web)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure Kestrel for high load
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 104_857_600; // 100MB
});

var app = builder.Build();

// ============================================================
// 2. MIDDLEWARE PIPELINE
// ============================================================

// Luôn hiện lỗi chi tiết để debug trên server
app.UseDeveloperExceptionPage();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("swagger/v1/swagger.json", "HR API v1");
    c.RoutePrefix = string.Empty;
});

app.UseCors("AllowAll");

// Tạm thời tắt Redirect để tránh lỗi SSL trên mạng nội bộ
// app.UseHttpsRedirection();

app.UseAuthorization();

// Endpoint kiểm tra kết nối DB nhanh
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
