using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Invoice_v1.IntegrationTests;

public class LogsIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;

    public LogsIntegrationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetLogs_ShouldReturnSeededLogs()
    {
        var adminId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"logAdm_{adminId.ToString()[..8]}@test.com", "logAdm",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            db.FileChangeLogs.Add(new FileChangeLog
            {
                FileName = "log-test-invoice.pdf",
                FileId = "log-test-file-id",
                ChangeType = "Upload",
                DetectedAt = DateTime.UtcNow,
                MimeType = "application/pdf",
                FileSize = 12345,
                Processed = false,
                SecurityStatus = "Healthy",
                UploadedByVendorId = adminId
            });
            await db.SaveChangesAsync();
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/logs");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("log-test-invoice.pdf");
    }

    [Fact]
    public async Task GetLogStats_ShouldReturnCorrectCounts()
    {
        var adminId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"statAdm_{adminId.ToString()[..8]}@test.com", "statAdm",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            // Add a few logs with different change types
            db.FileChangeLogs.Add(new FileChangeLog
            {
                FileName = "stat-upload.pdf",
                FileId = $"stat-file-{Guid.NewGuid().ToString()[..6]}",
                ChangeType = "Upload",
                DetectedAt = DateTime.UtcNow,
                Processed = true,
                SecurityStatus = "Healthy"
            });
            db.FileChangeLogs.Add(new FileChangeLog
            {
                FileName = "stat-modified.pdf",
                FileId = $"stat-file-{Guid.NewGuid().ToString()[..6]}",
                ChangeType = "Modified",
                DetectedAt = DateTime.UtcNow,
                Processed = false,
                SecurityStatus = "Healthy"
            });
            await db.SaveChangesAsync();
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/logs/stats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        // Should contain stats grouped by ChangeType
        content.Should().Contain("Upload");
    }
}
