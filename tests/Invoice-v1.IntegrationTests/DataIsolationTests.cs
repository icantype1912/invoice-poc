using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Invoice_v1.IntegrationTests;

public class DataIsolationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;

    public DataIsolationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task VendorA_ShouldNotSee_InvoicesFromVendorB()
    {
        var vendorAId = Guid.NewGuid();
        var vendorBId = Guid.NewGuid();
        var invoiceNumB = $"INV-B-{Guid.NewGuid().ToString().Substring(0, 8)}";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var sql = @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES (@p0, @p1, @p2, 'hash', 'salt', @p3, @p4, 0, false, NOW(), NOW())";

            await db.Database.ExecuteSqlRawAsync(sql, vendorAId, "a@test.com", "userA", (int)UserRole.Vendor, (int)UserStatus.Approved);
            await db.Database.ExecuteSqlRawAsync(sql, vendorBId, "b@test.com", "userB", (int)UserRole.Vendor, (int)UserStatus.Approved);

            db.Invoices.Add(new Invoice
            {
                Id = Guid.NewGuid(),
                UploadedByVendorId = vendorBId,
                InvoiceNumber = invoiceNumB,
                TotalAmount = 999.99m,
                DriveFileId = "b-file-id",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var tokenA = AuthHelper.GenerateTestJwt(vendorAId, UserRole.Vendor);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        // CHANGED to standard invoices endpoint which definitely exists
        var response = await _client.GetAsync("/api/invoices");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("999.99");
        json.Should().NotContain(invoiceNumB);
    }
}