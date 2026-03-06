using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Invoice_v1.IntegrationTests;

public class SignupEdgeCaseTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public SignupEdgeCaseTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Signup_ShouldFail_WithMissingFields()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/signup", new
        {
            Email = "",
            Password = "",
            CompanyName = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Signup_ShouldFail_WithDuplicateEmail()
    {
        var uniqueEmail = $"dup_{Guid.NewGuid().ToString()[..8]}@test.com";

        // First signup should succeed
        var first = await _client.PostAsJsonAsync("/api/auth/signup", new
        {
            Email = uniqueEmail,
            Password = "StrongP@ss123!",
            CompanyName = "Test Corp"
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second signup with the same email should fail
        var second = await _client.PostAsJsonAsync("/api/auth/signup", new
        {
            Email = uniqueEmail,
            Password = "AnotherP@ss456!",
            CompanyName = "Different Corp"
        });
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await second.Content.ReadAsStringAsync();
        content.Should().Contain("already registered");
    }
}
