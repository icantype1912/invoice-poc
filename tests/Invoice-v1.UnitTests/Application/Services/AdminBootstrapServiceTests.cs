using FluentAssertions;
using invoice_v1.src.Application.Security;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoice_v1.UnitTests;

public class AdminBootstrapServiceTests
{
    private ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private IConfiguration CreateConfig(string? email, string? password)
    {
        var settings = new Dictionary<string, string?>
        {
            ["AdminBootstrap:Email"] = email,
            ["AdminBootstrap:Password"] = password
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings!)
            .Build();
    }

    [Fact]
    public async Task EnsureAdminExistsAsync_ShouldDoNothing_WhenAdminExists()
    {
        var context = CreateDbContext();

        context.Users.Add(new User
        {
            Email = "admin@test.com",
            Role = UserRole.Admin,
            Status = UserStatus.Approved
        });

        await context.SaveChangesAsync();

        var service = new AdminBootstrapService(
            context,
            CreateConfig("admin@test.com", "password"),
            Mock.Of<IPasswordHasher>(),
            Mock.Of<ILogger<AdminBootstrapService>>());

        await service.EnsureAdminExistsAsync();

        context.Users.Count(u => u.Role == UserRole.Admin)
               .Should().Be(1);
    }

    [Fact]
    public async Task EnsureAdminExistsAsync_ShouldSkip_WhenConfigMissing()
    {
        var context = CreateDbContext();

        var service = new AdminBootstrapService(
            context,
            CreateConfig(null, null),
            Mock.Of<IPasswordHasher>(),
            Mock.Of<ILogger<AdminBootstrapService>>());

        await service.EnsureAdminExistsAsync();

        context.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureAdminExistsAsync_ShouldCreateAdmin_WhenNoneExists()
    {
        var context = CreateDbContext();

        var passwordHasherMock = new Mock<IPasswordHasher>();
        passwordHasherMock
            .Setup(p => p.HashPassword("password"))
            .Returns(("hash", "salt"));

        var service = new AdminBootstrapService(
            context,
            CreateConfig("Admin@Test.com", "password"),
            passwordHasherMock.Object,
            Mock.Of<ILogger<AdminBootstrapService>>());

        await service.EnsureAdminExistsAsync();

        var admin = await context.Users.FirstOrDefaultAsync();

        admin.Should().NotBeNull();
        admin!.Role.Should().Be(UserRole.Admin);
        admin.Status.Should().Be(UserStatus.Approved);
        admin.Email.Should().Be("admin@test.com"); // normalized
        admin.PasswordHash.Should().Be("hash");
        admin.PasswordSalt.Should().Be("salt");
    }

    [Fact]
    public async Task EnsureAdminExistsAsync_ShouldCreateAdmin_WhenOnlySoftDeletedExists()
    {
        var context = CreateDbContext();

        context.Users.Add(new User
        {
            Email = "old@test.com",
            Role = UserRole.Admin,
            IsSoftDeleted = true
        });

        await context.SaveChangesAsync();

        var passwordHasherMock = new Mock<IPasswordHasher>();
        passwordHasherMock
            .Setup(p => p.HashPassword("password"))
            .Returns(("hash", "salt"));

        var service = new AdminBootstrapService(
            context,
            CreateConfig("new@test.com", "password"),
            passwordHasherMock.Object,
            Mock.Of<ILogger<AdminBootstrapService>>());

        await service.EnsureAdminExistsAsync();

        context.Users.Count(u => u.Role == UserRole.Admin && !u.IsSoftDeleted)
               .Should().Be(1);
    }
}