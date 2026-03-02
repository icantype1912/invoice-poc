using FluentAssertions;
using invoice_v1.src.Application.Security;
using Xunit;

namespace Invoice_v1.UnitTests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_ShouldReturnHashAndSalt()
    {
        var (hash, salt) = _hasher.HashPassword("Password123!");

        hash.Should().NotBeNullOrWhiteSpace();
        salt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void HashPassword_ShouldReturnDifferentHash_ForSamePassword()
    {
        var result1 = _hasher.HashPassword("Password123!");
        var result2 = _hasher.HashPassword("Password123!");

        result1.Hash.Should().NotBe(result2.Hash);
        result1.Salt.Should().NotBe(result2.Salt);
    }

    [Fact]
    public void VerifyPassword_ShouldReturnTrue_ForCorrectPassword()
    {
        var password = "Password123!";
        var (hash, salt) = _hasher.HashPassword(password);

        var result = _hasher.VerifyPassword(password, hash, salt);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_ShouldReturnFalse_ForWrongPassword()
    {
        var (hash, salt) = _hasher.HashPassword("Password123!");

        var result = _hasher.VerifyPassword("WrongPassword", hash, salt);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_ShouldReturnFalse_WhenHashIsTampered()
    {
        var password = "Password123!";
        var (hash, salt) = _hasher.HashPassword(password);

        var tamperedHash = hash.Substring(0, hash.Length - 2) + "AA";

        var result = _hasher.VerifyPassword(password, tamperedHash, salt);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_ShouldReturnFalse_WhenSaltIsTampered()
    {
        var password = "Password123!";
        var (hash, salt) = _hasher.HashPassword(password);

        var tamperedSalt = salt.Substring(0, salt.Length - 2) + "AA";

        var result = _hasher.VerifyPassword(password, hash, tamperedSalt);

        result.Should().BeFalse();
    }

    [Fact]
    public void HashPassword_ShouldWork_WithEmptyPassword()
    {
        var (hash, salt) = _hasher.HashPassword("");

        hash.Should().NotBeNullOrWhiteSpace();
        salt.Should().NotBeNullOrWhiteSpace();

        var result = _hasher.VerifyPassword("", hash, salt);
        result.Should().BeTrue();
    }
}