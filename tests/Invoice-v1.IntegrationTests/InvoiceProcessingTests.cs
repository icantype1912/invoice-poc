using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Xunit;
using invoice_v1.src.Infrastructure.Data;
using invoice_v1.src.Domain.Entities;

namespace Invoice_v1.IntegrationTests;

public class InvoiceProcessingTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private const string TestSecret = "test-integration-secret-key-64-chars-long-for-hmac-validation";

    public InvoiceProcessingTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProcessCallback_ShouldHandleExistingProducts_WithoutDuplicates()
    {
        var existingProductId = "PROD-999";

        // Block 1: Seed Data
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Products.Add(new Product
            {
                Id = Guid.NewGuid(),
                ProductId = existingProductId,
                ProductName = "Old Name",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var jobId = Guid.NewGuid();
        var fileId = "file-id-for-product-test";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payload = JsonDocument.Parse(JsonSerializer.Serialize(new { fileId = fileId }));
            db.JobQueues.Add(new JobQueue { Id = jobId, PayloadJson = payload });
            await db.SaveChangesAsync();
        }

        // Block 2: Send Request
        var requestObj = new
        {
            JobId = jobId,
            Status = "COMPLETED",
            Result = new
            {
                InvoiceNumber = "INV-DUP-TEST",
                TotalAmount = 100.0,
                LineItems = new[] {
                    new { ProductId = existingProductId, ProductName = "Updated Name", Quantity = 1, UnitRate = 100, Amount = 100 }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestObj);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/callback");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Callback-HMAC", ComputeHmac(json));

        // FIX: Fresh client
        var client = _factory.CreateClient();
        await client.SendAsync(request);

        // Block 3: Verify Data
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // EF Core sometimes caches the entities. AsNoTracking forces a real DB query.
            var products = await db.Products
                .AsNoTracking()
                .Where(p => p.ProductId == existingProductId)
                .ToListAsync();

            products.Should().HaveCount(1);
            products.First().ProductName.Should().Be("Updated Name");
        }
    }

    private string ComputeHmac(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }
}