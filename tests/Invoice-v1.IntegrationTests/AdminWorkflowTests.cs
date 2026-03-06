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

public class AdminWorkflowTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;

    public AdminWorkflowTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Admin_ShouldSee_PendingUsers()
    {
        // Arrange: seed a pending vendor
        var vendorId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                vendorId, $"pending_{vendorId.ToString()[..8]}@test.com", "pendingUser",
                (int)UserRole.Vendor, (int)UserStatus.Pending);
        }

        var adminId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"admin_{adminId.ToString()[..8]}@test.com", "adminUser",
                (int)UserRole.Admin, (int)UserStatus.Approved);
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users/pending");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("pendingUser");
    }

    [Fact]
    public async Task Admin_ShouldApprove_PendingVendor()
    {
        var adminId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"adminAppr_{adminId.ToString()[..8]}@test.com", "adminAppr",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                vendorId, $"vendAppr_{vendorId.ToString()[..8]}@test.com", "vendAppr",
                (int)UserRole.Vendor, (int)UserStatus.Pending);
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{vendorId}/approve");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == vendorId);
            user.Should().NotBeNull();
            user!.Status.Should().Be(UserStatus.Approved);
        }
    }

    [Fact]
    public async Task Admin_ShouldReject_Vendor_WithReason()
    {
        var adminId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"adminRej_{adminId.ToString()[..8]}@test.com", "adminRej",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                vendorId, $"vendRej_{vendorId.ToString()[..8]}@test.com", "vendRej",
                (int)UserRole.Vendor, (int)UserStatus.Pending);
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{vendorId}/reject");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { Reason = "Invalid business documentation" });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == vendorId);
            user.Should().NotBeNull();
            user!.Status.Should().Be(UserStatus.Rejected);
        }
    }

    [Fact]
    public async Task Admin_ShouldSoftDelete_User()
    {
        var adminId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"adminDel_{adminId.ToString()[..8]}@test.com", "adminDel",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                vendorId, $"vendDel_{vendorId.ToString()[..8]}@test.com", "vendDel",
                (int)UserRole.Vendor, (int)UserStatus.Approved);
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/users/{vendorId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == vendorId);
            user.Should().NotBeNull();
            user!.IsSoftDeleted.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Admin_ShouldUnlock_LockedUser()
    {
        var adminId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"adminUnl_{adminId.ToString()[..8]}@test.com", "adminUnl",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                vendorId, $"vendUnl_{vendorId.ToString()[..8]}@test.com", "vendUnl",
                (int)UserRole.Vendor, (int)UserStatus.Locked);
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{vendorId}/unlock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == vendorId);
            user.Should().NotBeNull();
            user!.Status.Should().Be(UserStatus.Approved);
        }
    }
}
