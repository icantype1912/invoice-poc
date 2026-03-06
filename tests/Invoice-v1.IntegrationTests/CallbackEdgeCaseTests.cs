using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Invoice_v1.IntegrationTests;

public class CallbackEdgeCaseTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;
    private const string TestSecret = "test-integration-secret-key-64-chars-long-for-hmac-validation";

    public CallbackEdgeCaseTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private string ComputeHmac(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    [Fact]
    public async Task Callback_ShouldReject_WithoutHmacHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/callback");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        // No X-Callback-HMAC header

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Callback_ShouldReject_WithInvalidHmac()
    {
        var json = JsonSerializer.Serialize(new { JobId = Guid.NewGuid(), Status = "COMPLETED" });
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/callback");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Callback-HMAC", "invalid-hmac-value");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Callback_ShouldHandle_FailedJob()
    {
        var jobId = Guid.NewGuid();

        // Seed a PENDING job
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                fileId = "fail-test-file",
                originalName = "failed.pdf"
            }));
            db.JobQueues.Add(new JobQueue
            {
                Id = jobId,
                Status = "PENDING",
                PayloadJson = payload,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var callbackRequest = new
        {
            JobId = jobId,
            Status = "FAILED",
            Reason = "Worker could not extract invoice data"
        };

        var json = JsonSerializer.Serialize(callbackRequest);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/callback");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Callback-HMAC", ComputeHmac(json));

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify job is marked as INVALID (MarkFailedAsync transitions to INVALID)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var job = await db.JobQueues.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId);
            job.Should().NotBeNull();
            job!.Status.Should().Be("INVALID");
        }
    }

    [Fact]
    public async Task Callback_ShouldHandle_InvalidJob()
    {
        var jobId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payload = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                fileId = "invalid-test-file",
                originalName = "invalid.pdf"
            }));
            db.JobQueues.Add(new JobQueue
            {
                Id = jobId,
                Status = "PROCESSING",
                PayloadJson = payload,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var callbackRequest = new
        {
            JobId = jobId,
            Status = "INVALID",
            Reason = "File is not a valid invoice"
        };

        var json = JsonSerializer.Serialize(callbackRequest);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/callback");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Callback-HMAC", ComputeHmac(json));

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var job = await db.JobQueues.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId);
            job.Should().NotBeNull();
            job!.Status.Should().Be("INVALID");

            // Should also create an invalid_invoice entry
            var invalid = await db.InvalidInvoices.AsNoTracking()
                .FirstOrDefaultAsync(i => i.JobId == jobId);
            invalid.Should().NotBeNull();
        }
    }
}
