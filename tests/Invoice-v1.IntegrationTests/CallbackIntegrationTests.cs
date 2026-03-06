using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
// --- Essential Testing Libraries ---
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore; // FIX: Required for FirstOrDefaultAsync
using FluentAssertions;
using Xunit;

// --- Your Project Namespaces ---
using invoice_v1.src.Infrastructure.Data;
using invoice_v1.src.Domain.Entities;

namespace Invoice_v1.IntegrationTests;

public class CallbackIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;
    private const string TestSecret = "test-integration-secret-key-64-chars-long-for-hmac-validation";

    public CallbackIntegrationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        // FIX: Ensuring we use the WebApplicationFactory extension
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private string ComputeHmac(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }

    [Fact]
    public async Task HandleCallback_ValidCompletedJob_UpdatesDatabaseCorrectly()
    {
        // 1. ARRANGE
        var jobId = Guid.NewGuid();
        var fileId = "test-drive-file-id";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var payload = JsonDocument.Parse(JsonSerializer.Serialize(new { fileId = fileId, originalName = "test.pdf" }));

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

        // 2. ARRANGE: Prep request payload
        var callbackRequest = new
        {
            JobId = jobId,
            Status = "COMPLETED",
            Result = new
            {
                InvoiceNumber = "INV-100",
                TotalAmount = 1500.75,
                Currency = "USD",
                LineItems = new[] {
                    new {
                        ProductId = "P1",
                        ProductName = "Widget",
                        Quantity = 2.0,
                        UnitRate = 750.375,
                        Amount = 1500.75,
                        Category = "Hardware"
                    }
                }
            }
        };

        var jsonBody = JsonSerializer.Serialize(callbackRequest);
        var hmacHeader = ComputeHmac(jsonBody);

        // 3. ACT
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/callback");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Callback-HMAC", hmacHeader);

        var response = await _client.SendAsync(request);

        // 4. ASSERT
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var job = await db.JobQueues.FirstOrDefaultAsync(j => j.Id == jobId);
            job.Should().NotBeNull();
            job!.Status.Should().Be("COMPLETED");

            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.DriveFileId == fileId);
            invoice.Should().NotBeNull();
            invoice!.TotalAmount.Should().Be(1500.75m);
        }
    }
}