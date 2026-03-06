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

public class AnalyticsIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;

    public AnalyticsIntegrationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTrendingProducts_ShouldReflectSeededData()
    {
        var vendorId = Guid.NewGuid();
        var productId = "TECH-001";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // FIX: Added the User seed to prevent Foreign Key errors
            var sql = @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES (@p0, @p1, @p2, 'hash', 'salt', @p3, @p4, 0, false, NOW(), NOW())";
            await db.Database.ExecuteSqlRawAsync(sql, vendorId, "vend2@test.com", "vendorY", (int)UserRole.Vendor, (int)UserStatus.Approved);

            var product = new Product { ProductId = productId, ProductName = "Integration Test Laptop" };
            db.Products.Add(product);

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                UploadedByVendorId = vendorId,
                TotalAmount = 2000,
                InvoiceDate = DateTime.UtcNow.AddDays(-1),
                LineItems = new List<InvoiceLine>
                {
                    new InvoiceLine { ProductGuid = product.Id, ProductId = productId, ProductName = product.ProductName, Quantity = 2, UnitRate = 1000, Amount = 2000 }
                }
            };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        var token = AuthHelper.GenerateTestJwt(vendorId, UserRole.Vendor);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var startDate = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var endDate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ");

        var response = await _client.GetAsync($"/api/analytics/products/trending?startDate={startDate}&endDate={endDate}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain(productId);
        content.Should().Contain("2000");
    }
}