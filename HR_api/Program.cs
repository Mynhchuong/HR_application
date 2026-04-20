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

// Always show Swagger in this demo/dev phase
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "HR API v1");
    c.RoutePrefix = string.Empty; // Set Swagger as the default page
});

app.UseCors("AllowAll");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
