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
