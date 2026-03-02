using FluentAssertions;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Security;
using invoice_v1.src.Application.Services;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Invoice_v1.UnitTests;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenService> _jwtMock = new();
    private readonly Mock<ILogger<AuthService>> _loggerMock = new();

    private AuthService CreateService()
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            AccessTokenMinutes = 60
        });

        return new AuthService(
            _userRepoMock.Object,
            _passwordHasherMock.Object,
            _jwtMock.Object,
            jwtOptions,
            _loggerMock.Object);
    }

    [Fact]
    public async Task SignupAsync_ShouldThrow_WhenEmailAlreadyRegistered()
    {
        // Arrange
        var request = new SignupRequest
        {
            Email = "test@test.com",
            Password = "password",
            CompanyName = "TestCo"
        };

        _userRepoMock
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync(new User());

        var service = CreateService();

        // Act
        Func<Task> act = async () => await service.SignupAsync(request);

        // Assert
        await act.Should()
                 .ThrowAsync<InvalidOperationException>()
                 .WithMessage("Email is already registered");
    }

    [Fact]
    public async Task SignupAsync_ShouldThrow_WhenUserIsSoftDeleted()
    {
        // Arrange
        var request = new SignupRequest
        {
            Email = "deleted@test.com",
            Password = "password",
            CompanyName = "TestCo"
        };

        var existingUser = new User
        {
            Email = request.Email,
            IsSoftDeleted = true
        };

        _userRepoMock
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync(existingUser);

        var service = CreateService();

        // Act
        Func<Task> act = async () => await service.SignupAsync(request);

        // Assert
        await act.Should()
                 .ThrowAsync<InvalidOperationException>()
                 .WithMessage("Prohibited to register contact admin");
    }

    [Fact]
    public async Task SignupAsync_FirstUser_ShouldBeAdminAndApproved()
    {
        // Arrange
        var request = new SignupRequest
        {
            Email = "admin@test.com",
            Password = "password",
            CompanyName = "AdminCo"
        };

        _userRepoMock
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        _userRepoMock
            .Setup(r => r.AnyAdminExistsAsync())
            .ReturnsAsync(false);

        _passwordHasherMock
            .Setup(p => p.HashPassword(request.Password))
            .Returns(("hash", "salt"));

        User? createdUser = null;

        _userRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<User>()))
            .Callback<User>(u => createdUser = u)
            .ReturnsAsync((User u) => u);

        _userRepoMock
            .Setup(r => r.SaveChangesAsync())
            .ReturnsAsync(1);

        var service = CreateService();

        // Act
        await service.SignupAsync(request);

        // Assert
        createdUser.Should().NotBeNull();
        createdUser!.Role.Should().Be(UserRole.Admin);
        createdUser.Status.Should().Be(UserStatus.Approved);
    }

    [Fact]
    public async Task SignupAsync_WhenAdminExists_ShouldCreateVendorWithPendingStatus()
    {
        // Arrange
        var request = new SignupRequest
        {
            Email = "vendor@test.com",
            Password = "password",
            CompanyName = "VendorCo"
        };

        _userRepoMock
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        _userRepoMock
            .Setup(r => r.AnyAdminExistsAsync())
            .ReturnsAsync(true); // Admin already exists

        _passwordHasherMock
            .Setup(p => p.HashPassword(request.Password))
            .Returns(("hash", "salt"));

        User? createdUser = null;

        _userRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<User>()))
            .Callback<User>(u => createdUser = u)
            .ReturnsAsync((User u) => u);

        _userRepoMock
            .Setup(r => r.SaveChangesAsync())
            .ReturnsAsync(1);

        var service = CreateService();

        // Act
        await service.SignupAsync(request);

        // Assert
        createdUser.Should().NotBeNull();
        createdUser!.Role.Should().Be(UserRole.Vendor);
        createdUser.Status.Should().Be(UserStatus.Pending);
    }

    [Fact]
    public async Task LoginAsync_ShouldThrow_WhenUserDoesNotExist()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "missing@test.com",
            Password = "password"
        };

        _userRepoMock
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        var service = CreateService();

        // Act
        Func<Task> act = async () => await service.LoginAsync(request);

        // Assert
        await act.Should()
                 .ThrowAsync<UnauthorizedAccessException>()
                 .WithMessage("Invalid email or password");
    }

    [Fact]
    public async Task LoginAsync_ShouldThrow_WhenPasswordIsInvalid()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "user@test.com",
            Password = "wrongpassword"
        };

        var user = new User
        {
            Email = request.Email,
            PasswordHash = "storedHash",
            PasswordSalt = "storedSalt",
            Status = UserStatus.Approved
        };

        _userRepoMock
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(p => p.VerifyPassword(
                request.Password,
                user.PasswordHash,
                user.PasswordSalt))
            .Returns(false); // Password check fails

        var service = CreateService();

        // Act
        Func<Task> act = async () => await service.LoginAsync(request);

        // Assert
        await act.Should()
                 .ThrowAsync<UnauthorizedAccessException>()
                 .WithMessage("Invalid email or password");
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnToken_WhenCredentialsAreValid()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "user@test.com",
            Password = "correctpassword"
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = "storedHash",
            PasswordSalt = "storedSalt",
            Status = UserStatus.Approved,
            Role = UserRole.Vendor,
            CompanyName = "TestCo",
            FailedLoginCount = 5 // simulate previous failures
        };

        _userRepoMock
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(p => p.VerifyPassword(
                request.Password,
                user.PasswordHash,
                user.PasswordSalt))
            .Returns(true); // Password is valid

        _jwtMock
            .Setup(j => j.GenerateAccessToken(user))
            .Returns("fake-jwt-token");

        _userRepoMock
            .Setup(r => r.UpdateAsync(user))
            .Returns(Task.CompletedTask);

        _userRepoMock
            .Setup(r => r.SaveChangesAsync())
            .ReturnsAsync(1);

        var service = CreateService();

        // Act
        var result = await service.LoginAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().Be("fake-jwt-token");
        result.User.Email.Should().Be(user.Email);
        result.User.CompanyName.Should().Be(user.CompanyName);
        result.User.Role.Should().Be(user.Role.ToString());
        result.User.Status.Should().Be(user.Status.ToString());

        user.FailedLoginCount.Should().Be(0); // Reset after login

        _userRepoMock.Verify(r => r.UpdateAsync(user), Times.Once);
        _userRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_ShouldThrow_WhenUserIsSoftDeleted()
    {
        var request = new LoginRequest
        {
            Email = "deleted@test.com",
            Password = "password"
        };

        var user = new User
        {
            Email = request.Email,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            Status = UserStatus.Approved,
            IsSoftDeleted = true
        };

        _userRepoMock.Setup(r => r.GetByEmailAsync(request.Email))
                     .ReturnsAsync(user);

        _passwordHasherMock.Setup(p => p.VerifyPassword(
            request.Password,
            user.PasswordHash,
            user.PasswordSalt))
            .Returns(true);

        var service = CreateService();

        Func<Task> act = async () => await service.LoginAsync(request);

        await act.Should()
                 .ThrowAsync<UnauthorizedAccessException>()
                 .WithMessage("Account has been deleted");
    }

    [Fact]
    public async Task LoginAsync_ShouldThrow_WhenUserIsLocked()
    {
        var request = new LoginRequest
        {
            Email = "locked@test.com",
            Password = "password"
        };

        var user = new User
        {
            Email = request.Email,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            Status = UserStatus.Locked
        };

        _userRepoMock.Setup(r => r.GetByEmailAsync(request.Email))
                     .ReturnsAsync(user);

        _passwordHasherMock.Setup(p => p.VerifyPassword(
            request.Password,
            user.PasswordHash,
            user.PasswordSalt))
            .Returns(true);

        var service = CreateService();

        Func<Task> act = async () => await service.LoginAsync(request);

        await act.Should()
                 .ThrowAsync<UnauthorizedAccessException>()
                 .WithMessage("Account is locked. Contact admin to unlock.");
    }

    [Fact]
    public async Task LoginAsync_ShouldThrow_WhenUserIsPending()
    {
        var request = new LoginRequest
        {
            Email = "pending@test.com",
            Password = "password"
        };

        var user = new User
        {
            Email = request.Email,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            Status = UserStatus.Pending
        };

        _userRepoMock.Setup(r => r.GetByEmailAsync(request.Email))
                     .ReturnsAsync(user);

        _passwordHasherMock.Setup(p => p.VerifyPassword(
            request.Password,
            user.PasswordHash,
            user.PasswordSalt))
            .Returns(true);

        var service = CreateService();

        Func<Task> act = async () => await service.LoginAsync(request);

        await act.Should()
                 .ThrowAsync<UnauthorizedAccessException>()
                 .WithMessage("Account is pending approval");
    }

    [Fact]
    public async Task LoginAsync_ShouldThrow_WhenUserIsRejected()
    {
        var request = new LoginRequest
        {
            Email = "rejected@test.com",
            Password = "password"
        };

        var user = new User
        {
            Email = request.Email,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            Status = UserStatus.Rejected,
            RejectionReason = "Incomplete documents"
        };

        _userRepoMock.Setup(r => r.GetByEmailAsync(request.Email))
                     .ReturnsAsync(user);

        _passwordHasherMock.Setup(p => p.VerifyPassword(
            request.Password,
            user.PasswordHash,
            user.PasswordSalt))
            .Returns(true);

        var service = CreateService();

        Func<Task> act = async () => await service.LoginAsync(request);

        await act.Should()
                 .ThrowAsync<UnauthorizedAccessException>()
                 .WithMessage("Account registration was rejected. Reason: Incomplete documents");
    }
}