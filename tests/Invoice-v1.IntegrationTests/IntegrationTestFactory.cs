using invoice_v1.src.Infrastructure.Data;
using invoice_v1.src.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;
using System.IO;

namespace Invoice_v1.IntegrationTests;

public class IntegrationTestFactory : WebApplicationFactory<IApiMarker>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer;

    public IntegrationTestFactory()
    {
        _dbContainer = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .WithDatabase("invoice_test")
            .WithUsername("postgres")
            .WithPassword("password")
            .Build();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../backend"));
        builder.UseContentRoot(projectDir);
        builder.UseEnvironment("Testing");

        // Auth Settings
        builder.UseSetting("Security:CallbackSecret", "test-integration-secret-key-64-chars-long-for-hmac-validation");
        builder.UseSetting("Jwt:Secret", "test-jwt-secret-key-at-least-32-characters-long-for-signing");
        builder.UseSetting("Jwt:Issuer", "invoice-v1-test");
        builder.UseSetting("Jwt:Audience", "invoice-v1-test-users");
        builder.UseSetting("Jwt:AccessTokenMinutes", "60");

        // Force ADO.NET/Dapper to use the Docker Database
        builder.UseSetting("ConnectionStrings:DefaultConnection", _dbContainer.GetConnectionString());

        // AdminBootstrap settings so EnsureAdminExistsAsync creates an admin
        // This is critical: without a seeded admin, AuthService.SignupAsync treats
        // the first signup as Admin (auto-approved), breaking UserLifecycleTests.
        builder.UseSetting("AdminBootstrap:Email", "admin@integration-test.com");
        builder.UseSetting("AdminBootstrap:Password", "IntTest!Admin#2026");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(_dbContainer.GetConnectionString()));
        });
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        // Use MigrateAsync (not EnsureCreatedAsync!) to create schema WITH
        // __EFMigrationsHistory entries. This is critical because Program.cs also
        // calls MigrateAsync on startup — if we used EnsureCreatedAsync instead,
        // there would be no migration history, so MigrateAsync would throw 42P07
        // ("relation already exists"), and AdminBootstrapService.EnsureAdminExistsAsync
        // (which runs AFTER MigrateAsync in Program.cs) would never execute.
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(_dbContainer.GetConnectionString());
        using var db = new ApplicationDbContext(optionsBuilder.Options);

        await db.Database.MigrateAsync();

        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS search_logs (
                ""Id"" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                ""UserId"" uuid,
                ""NaturalLanguageQuery"" text,
                ""DetectedAt"" timestamp with time zone
            )");
    }

    public new async Task DisposeAsync() => await _dbContainer.StopAsync();
}