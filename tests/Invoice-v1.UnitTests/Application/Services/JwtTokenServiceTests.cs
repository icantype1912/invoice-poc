using FluentAssertions;
using invoice_v1.src.Application.Security;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Xunit;
using System.Text;

namespace Invoice_v1.UnitTests;

public class JwtTokenServiceTests
{
    private JwtTokenService CreateService()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "THIS_IS_A_SUPER_SECRET_KEY_12345678912344567876543212",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenMinutes = 60
        });

        return new JwtTokenService(options);
    }

    [Fact]
    public void GenerateAccessToken_ShouldReturnValidJwtToken()
    {
        var service = CreateService();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            Role = UserRole.Admin,
            Status = UserStatus.Approved
        };

        var tokenString = service.GenerateAccessToken(user);

        tokenString.Should().NotBeNullOrWhiteSpace();

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(tokenString);

        token.Issuer.Should().Be("TestIssuer");
        token.Audiences.Should().Contain("TestAudience");

        token.Claims.Should().Contain(c =>
            c.Type == JwtRegisteredClaimNames.Sub &&
            c.Value == user.Id.ToString());

        token.Claims.Should().Contain(c =>
            c.Type == JwtRegisteredClaimNames.Email &&
            c.Value == user.Email);

        token.Claims.Should().Contain(c =>
            c.Type == System.Security.Claims.ClaimTypes.Role &&
            c.Value == user.Role.ToString());

        token.Claims.Should().Contain(c =>
            c.Type == "status" &&
            c.Value == user.Status.ToString());

        token.ValidTo.Should().BeCloseTo(
            DateTime.UtcNow.AddMinutes(60),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GenerateAccessToken_ShouldBeCryptographicallyValid()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "THIS_IS_A_SUPER_SECRET_KEY_123456789",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenMinutes = 60
        });

        var service = new JwtTokenService(options);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            Role = UserRole.Admin,
            Status = UserStatus.Approved
        };

        var tokenString = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "TestIssuer",

            ValidateAudience = true,
            ValidAudience = "TestAudience",

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(options.Value.Secret)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var principal = handler.ValidateToken(tokenString, validationParams, out var validatedToken);

        principal.Should().NotBeNull();
    }

    [Fact]
    public void TokenValidation_ShouldFail_WithWrongSecret()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "CORRECT_SECRET_123456789123456787654323456",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenMinutes = 60
        });

        var service = new JwtTokenService(options);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            Role = UserRole.Admin,
            Status = UserStatus.Approved
        };

        var token = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();

        var invalidParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("WRONG_SECRET")),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false
        };

        Action act = () =>
            handler.ValidateToken(token, invalidParams, out _);

        act.Should().Throw<SecurityTokenInvalidSignatureException>();
    }

    [Fact]
    public void TokenValidation_ShouldFail_WithWrongIssuer()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "THIS_IS_A_SUPER_SECRET_KEY_123456789",
            Issuer = "CorrectIssuer",
            Audience = "TestAudience",
            AccessTokenMinutes = 60
        });

        var service = new JwtTokenService(options);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            Role = UserRole.Admin,
            Status = UserStatus.Approved
        };

        var token = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "WrongIssuer",

            ValidateAudience = false,
            ValidateLifetime = false,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(options.Value.Secret)),

            ClockSkew = TimeSpan.Zero
        };

        Action act = () =>
            handler.ValidateToken(token, validationParams, out _);

        act.Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public void TokenValidation_ShouldFail_WithWrongAudience()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "THIS_IS_A_SUPER_SECRET_KEY_123456789",
            Issuer = "TestIssuer",
            Audience = "CorrectAudience",
            AccessTokenMinutes = 60
        });

        var service = new JwtTokenService(options);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            Role = UserRole.Admin,
            Status = UserStatus.Approved
        };

        var token = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();

        var validationParams = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = "WrongAudience",

            ValidateIssuer = false,
            ValidateLifetime = false,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(options.Value.Secret)),

            ClockSkew = TimeSpan.Zero
        };

        Action act = () =>
            handler.ValidateToken(token, validationParams, out _);

        act.Should().Throw<SecurityTokenInvalidAudienceException>();
    }

    [Fact]
    public void TokenValidation_ShouldFail_WhenExpired()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "THIS_IS_A_SUPER_SECRET_KEY_123456789",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenMinutes = -1 // Already expired
        });

        var service = new JwtTokenService(options);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            Role = UserRole.Admin,
            Status = UserStatus.Approved
        };

        var token = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();

        var validationParams = new TokenValidationParameters
        {
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,

            ValidateIssuer = false,
            ValidateAudience = false,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(options.Value.Secret))
        };

        Action act = () =>
            handler.ValidateToken(token, validationParams, out _);

        act.Should().Throw<SecurityTokenExpiredException>();
    }

    [Fact]
    public void GenerateAccessToken_ShouldThrow_WhenSecretTooShort()
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = "short",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenMinutes = 60
        });

        var service = new JwtTokenService(options);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            Role = UserRole.Admin,
            Status = UserStatus.Approved
        };

        Action act = () => service.GenerateAccessToken(user);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}