using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Invoice_v1.IntegrationTests;

public class UserLifecycleTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;

    public UserLifecycleTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Vendor_ShouldNotBeAbleToLogin_UntilAdminApproves()
    {
        // THE FIX: Dynamic email prevents database collisions in parallel test runs
        var uniqueEmail = $"newvendor_{Guid.NewGuid()}@example.com";

        var signupRequest = new SignupRequest
        {
            Email = uniqueEmail,
            Password = "T3ch!V3ndor#2026",
            CompanyName = "Test Vendor Corp"
        };

        // 1. SIGNUP
        var signupResponse = await _client.PostAsJsonAsync("/api/auth/signup", signupRequest);
        signupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. LOGIN FAILS (Pending)
        var loginRequest = new LoginRequest { Email = signupRequest.Email, Password = signupRequest.Password };
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 3. APPROVE VIA DIRECT DATABASE (Bypasses EF HTTP cache)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == signupRequest.Email);

            user.Status = UserStatus.Approved;
            await db.SaveChangesAsync();
        }

        // 4. LOGIN SUCCEEDS (Active)
        var finalLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        finalLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}