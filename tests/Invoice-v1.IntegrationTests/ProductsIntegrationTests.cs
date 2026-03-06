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

public class ProductsIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;

    public ProductsIntegrationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetProducts_ShouldReturnSeededProducts()
    {
        var vendorId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                vendorId, $"prodVend_{vendorId.ToString()[..8]}@test.com", "prodVend",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            db.Products.Add(new Product
            {
                Id = productId,
                ProductId = $"PROD-{Guid.NewGuid().ToString()[..6]}",
                ProductName = "Integration Test Widget",
                Category = "TestCategory",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var token = AuthHelper.GenerateTestJwt(vendorId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Integration Test Widget");
    }

    [Fact]
    public async Task GetProductById_ShouldWork()
    {
        var adminId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var productCode = $"PROD-BY-ID-{Guid.NewGuid().ToString()[..6]}";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"prodAdm_{adminId.ToString()[..8]}@test.com", "prodAdm",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            db.Products.Add(new Product
            {
                Id = productId,
                ProductId = productCode,
                ProductName = "Specific Product",
                Category = "Electronics",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{productId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Specific Product");
    }

    [Fact]
    public async Task GetCategories_ShouldReturnDistinctCategories()
    {
        var adminId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" 
                (""Id"", ""Email"", ""Username"", ""PasswordHash"", ""PasswordSalt"", ""Role"", ""Status"", ""FailedLoginCount"", ""IsSoftDeleted"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES ({0}, {1}, {2}, 'hash', 'salt', {3}, {4}, 0, false, NOW(), NOW())",
                adminId, $"catAdm_{adminId.ToString()[..8]}@test.com", "catAdm",
                (int)UserRole.Admin, (int)UserStatus.Approved);

            db.Products.Add(new Product
            {
                Id = Guid.NewGuid(),
                ProductId = $"CAT-PROD-{Guid.NewGuid().ToString()[..6]}",
                ProductName = "Category Test Product",
                Category = "UniqueTestCategory",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var token = AuthHelper.GenerateTestJwt(adminId, UserRole.Admin);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/products/categories");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("UniqueTestCategory");
    }
}
