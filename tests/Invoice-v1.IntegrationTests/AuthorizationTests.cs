using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Invoice_v1.IntegrationTests;

public class AuthorizationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;

    public AuthorizationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AdminEndpoint_WhenAccessedByVendor_ShouldReturnForbidden()
    {
        var vendorId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // FIX: Seed the Vendor user
            db.Database.ExecuteSqlRaw(@"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, 'vendAuth1@test.com', 'vendAuth1', 'hash', 'salt', {1}, {2}, 0, false, NOW(), NOW())",
                vendorId, (int)UserRole.Vendor, (int)UserStatus.Approved);
        }

        var token = AuthHelper.GenerateTestJwt(vendorId, UserRole.Vendor);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users/pending");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task VendorEndpoint_WithValidToken_ShouldReturnSuccess()
    {
        var vendorId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.ExecuteSqlRaw(@"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, 'vendAuth2@test.com', 'vendAuth2', 'hash', 'salt', {1}, {2}, 0, false, NOW(), NOW())",
                vendorId, (int)UserRole.Vendor, (int)UserStatus.Approved);
        }

        var token = AuthHelper.GenerateTestJwt(vendorId, UserRole.Vendor);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/invoices");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}