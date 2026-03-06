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

public class InvalidInvoiceTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;

    public InvalidInvoiceTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetInvalidInvoices_ShouldReturn_WhenSeeded()
    {
        var adminId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"invAdm_{adminId.ToString()[..8]}@test.com", "invAdm",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            db.InvalidInvoices.Add(new InvalidInvoice
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                FileId = "invalid-file-id",
                FileName = "bad-invoice.pdf",
                VendorId = vendorId,
                Reason = JsonDocument.Parse(JsonSerializer.Serialize(new { message = "Corrupt file" })),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/invalid-invoices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("bad-invoice.pdf");
    }

    [Fact]
    public async Task Vendor_ShouldNotBeAble_ToRequeue()
    {
        var vendorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                vendorId, $"vendReq_{vendorId.ToString()[..8]}@test.com", "vendReq",
                (int)UserRole.Vendor, (int)UserStatus.Approved);
        }

        var token = AuthHelper.GenerateTestJwt(vendorId, UserRole.Vendor);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/invalid-invoices/{jobId}/requeue");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        // Vendor should be forbidden from requeueing — Admin only
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
