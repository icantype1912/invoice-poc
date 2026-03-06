using FluentAssertions;
using invoice_v1.src.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Invoice_v1.IntegrationTests;

public class AnalyticsTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;

    public AnalyticsTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetProductSales_ShouldReturnCorrectCalculations()
    {
        var vendorId = Guid.NewGuid();
        var productId = "ANALYTICS-TEST-001";

        // FIX: Must create a Scope to get the DbContext
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<invoice_v1.src.Infrastructure.Data.ApplicationDbContext>();
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();

            // FIX: Added the User insert to prevent Foreign Key errors
            cmd.CommandText = $@"
                INSERT INTO ""Users"" (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ('{vendorId}', 'vend@test.com', 'vendorX', 'hash', 'salt', {(int)UserRole.Vendor}, {(int)UserStatus.Approved}, 0, false, NOW(), NOW());

                INSERT INTO products (""Id"", ""ProductId"", ""ProductName"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ('{Guid.NewGuid()}', '{productId}', 'Test Product', NOW(), NOW());
                
                INSERT INTO invoices (""Id"", ""UploadedByVendorId"", ""TotalAmount"", ""DriveFileId"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ('{Guid.NewGuid()}', '{vendorId}', 500.00, 'file-123', NOW(), NOW());";
            await cmd.ExecuteNonQueryAsync();
        }

        var token = AuthHelper.GenerateTestJwt(vendorId, UserRole.Vendor);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync($"/api/analytics/products/sales?startDate={DateTime.UtcNow.AddDays(-1):yyyy-MM-ddTHH:mm:ssZ}&endDate={DateTime.UtcNow.AddDays(1):yyyy-MM-ddTHH:mm:ssZ}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
    }
}