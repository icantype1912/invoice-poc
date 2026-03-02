using invoice_v1.src.Api.Middleware;
using invoice_v1.src.Application.BackgroundServices;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Security;
using invoice_v1.src.Application.Services;
using invoice_v1.src.Infrastructure.Data;
using invoice_v1.src.Infrastructure.Repositories;
using invoice_v1.src.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// DATABASE
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null);
        npgsqlOptions.CommandTimeout(30);
    }));

// CACHE
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "InvoiceApp_";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// AUTH
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtSecret = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured");

builder.Services.Configure<JwtOptions>(jwtSettings);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(5)
        };
    });

builder.Services.AddAuthorization();

// DEPENDENCY INJECTION
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<IFileChangeLogRepository, FileChangeLogRepository>();
builder.Services.AddScoped<IInvalidInvoiceRepository, InvalidInvoiceRepository>();
builder.Services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
builder.Services.AddScoped<IDbContextFacade, DbContextFacade>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IVendorInvoiceService, VendorInvoiceService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IFileChangeLogService, FileChangeLogService>();
builder.Services.AddScoped<IInvalidInvoiceService, InvalidInvoiceService>();
builder.Services.AddScoped<IRateLimitService, RateLimitService>();

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IHmacValidator, HmacValidator>();
builder.Services.AddSingleton<IGoogleDriveService, GoogleDriveService>();

builder.Services.AddHttpClient();

// SECURITY PIPELINE
builder.Services.AddSingleton<FileTypeValidator>();
builder.Services.AddSingleton<MagicBytesValidator>();
builder.Services.AddScoped<TokenCountValidator>();
builder.Services.AddScoped<IFileSecurityPipeline, FileSecurityPipeline>();
builder.Services.AddHttpClient<VirusTotalScanner>(client =>
{
    client.BaseAddress = new Uri("https://www.virustotal.com");
    client.Timeout = TimeSpan.FromSeconds(30);
});


// BACKGROUND SERVICES
builder.Services.AddHostedService<JobCreationService>();
builder.Services.AddHostedService<JobRetryService>();
builder.Services.AddHostedService<DriveMonitoringService>();
builder.Services.AddScoped<AdminBootstrapService>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// CONTROLLERS
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// SWAGGER (FIXED - AUTOMATIC BEARER)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Invoice API", Version = "v1" });

    // Fix: JsonDocument/JsonElement cannot be described by Swashbuckle.
    // Map them to a freeform object schema so swagger.json generates correctly.
    c.MapType<System.Text.Json.JsonDocument>(() => new OpenApiSchema { Type = "object", AdditionalPropertiesAllowed = true });
    c.MapType<System.Text.Json.JsonElement>(() => new OpenApiSchema { Type = "object", AdditionalPropertiesAllowed = true });

    // Use 'Http' scheme with 'bearer' format - No manual 'Bearer ' prefix needed
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter your JWT token in the text input below (Do NOT type 'Bearer').",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// LOGGING - Serilog with status-based file sinks
builder.Host.UseSerilog((context, config) =>
{
    config
        .MinimumLevel.Debug()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        // Console output
        .WriteTo.Console()
        // Success logs (Info and below)
        .WriteTo.Logger(lc => lc
            .Filter.ByIncludingOnly(e => e.Level <= LogEventLevel.Information)
            .WriteTo.File(
                path: Path.Combine("logs", "logs_backend", "success", "log-.txt"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} | {SourceContext} | {Level:u3} | {Message:lj}{NewLine}{Exception}"))
        // Warn logs (Warning only)
        .WriteTo.Logger(lc => lc
            .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Warning)
            .WriteTo.File(
                path: Path.Combine("logs", "logs_backend", "warn", "log-.txt"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} | {SourceContext} | {Level:u3} | {Message:lj}{NewLine}{Exception}"))
        // Fail logs (Error and Fatal)
        .WriteTo.Logger(lc => lc
            .Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Error)
            .WriteTo.File(
                path: Path.Combine("logs", "logs_backend", "fail", "log-.txt"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} | {SourceContext} | {Level:u3} | {Message:lj}{NewLine}{Exception}"));
});

var app = builder.Build();

// MIGRATION
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        if (context.Database.GetPendingMigrations().Any())
        {
            await context.Database.MigrateAsync();
        }
        await services.GetRequiredService<AdminBootstrapService>().EnsureAdminExistsAsync();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred during migration.");
    }
}

// MIDDLEWARE
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseCors("AllowAngular");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
