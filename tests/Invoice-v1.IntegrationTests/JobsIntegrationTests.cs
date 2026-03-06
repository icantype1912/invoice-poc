using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Invoice_v1.IntegrationTests;

public class JobsIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;

    public JobsIntegrationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetJobs_ShouldReturnSeededJobs()
    {
        var adminId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"jobAdmin_{adminId.ToString()[..8]}@test.com", "jobAdmin",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            var payload = JsonDocument.Parse(JsonSerializer.Serialize(new { fileId = "job-list-file" }));
            db.JobQueues.Add(new JobQueue
            {
                Id = jobId,
                JobType = "INVOICE_EXTRACTION",
                Status = "PENDING",
                PayloadJson = payload,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/jobs");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("PENDING");
    }

    [Fact]
    public async Task GetJobById_ShouldReturn_WhenJobExists()
    {
        var adminId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"jobById_{adminId.ToString()[..8]}@test.com", "jobById",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            var payload = JsonDocument.Parse(JsonSerializer.Serialize(new { fileId = "job-detail-file" }));
            db.JobQueues.Add(new JobQueue
            {
                Id = jobId,
                JobType = "INVOICE_EXTRACTION",
                Status = "COMPLETED",
                PayloadJson = payload,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/jobs/{jobId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain(jobId.ToString());
    }

    [Fact]
    public async Task Vendor_ShouldOnlySee_OwnJobs()
    {
        var vendorId = Guid.NewGuid();
        var otherVendorId = Guid.NewGuid();
        var ownJobId = Guid.NewGuid();
        var otherJobId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                vendorId, $"vendJob_{vendorId.ToString()[..8]}@test.com", "vendJob",
                (int)UserRole.Vendor, (int)UserStatus.Approved);

            // Own job
            var ownPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                fileId = "own-file",
                uploader = vendorId.ToString()
            }));
            db.JobQueues.Add(new JobQueue
            {
                Id = ownJobId,
                JobType = "INVOICE_EXTRACTION",
                Status = "PENDING",
                PayloadJson = ownPayload,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            // Other vendor's job
            var otherPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                fileId = "other-file",
                uploader = otherVendorId.ToString()
            }));
            db.JobQueues.Add(new JobQueue
            {
                Id = otherJobId,
                JobType = "INVOICE_EXTRACTION",
                Status = "PENDING",
                PayloadJson = otherPayload,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        // Vendor should NOT be able to access the other vendor's job
        var token = AuthHelper.GenerateTestJwt(vendorId, UserRole.Vendor);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/jobs/{otherJobId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        // Should be 403 Forbidden since vendor doesn't own this job
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }
}
