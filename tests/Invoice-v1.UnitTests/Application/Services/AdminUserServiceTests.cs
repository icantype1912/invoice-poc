using FluentAssertions;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Services;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoice_v1.UnitTests;

public class AdminUserServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ILogger<AdminUserService>> _loggerMock = new();

    private AdminUserService CreateService()
    {
        return new AdminUserService(
            _userRepoMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ApproveUserAsync_ShouldThrow_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId))
                     .ReturnsAsync((User?)null);

        var service = CreateService();

        Func<Task> act = async () =>
            await service.ApproveUserAsync(userId, Guid.NewGuid());

        await act.Should()
                 .ThrowAsync<InvalidOperationException>()
                 .WithMessage($"User {userId} not found");
    }

    [Fact]
    public async Task ApproveUserAsync_ShouldThrow_WhenUserNotPending()
    {
        var userId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            Status = UserStatus.Approved
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId))
                     .ReturnsAsync(user);

        var service = CreateService();

        Func<Task> act = async () =>
            await service.ApproveUserAsync(userId, Guid.NewGuid());

        await act.Should()
                 .ThrowAsync<InvalidOperationException>()
                 .WithMessage("User is not in Pending status");
    }

    [Fact]
    public async Task ApproveUserAsync_ShouldApproveUser_WhenPending()
    {
        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            Status = UserStatus.Pending
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId))
                     .ReturnsAsync(user);

        _userRepoMock.Setup(r => r.UpdateAsync(user))
                     .Returns(Task.CompletedTask);

        _userRepoMock.Setup(r => r.SaveChangesAsync())
                     .ReturnsAsync(1);

        var service = CreateService();

        await service.ApproveUserAsync(userId, adminId);

        user.Status.Should().Be(UserStatus.Approved);
        user.ApprovedByAdminId.Should().Be(adminId);

        _userRepoMock.Verify(r => r.UpdateAsync(user), Times.Once);
        _userRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task RejectUserAsync_ShouldReject_WhenPending()
    {
        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            Status = UserStatus.Pending
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId))
                     .ReturnsAsync(user);

        _userRepoMock.Setup(r => r.UpdateAsync(user))
                     .Returns(Task.CompletedTask);

        _userRepoMock.Setup(r => r.SaveChangesAsync())
                     .ReturnsAsync(1);

        var service = CreateService();

        await service.RejectUserAsync(userId, adminId, "Incomplete docs");

        user.Status.Should().Be(UserStatus.Rejected);
        user.RejectionReason.Should().Be("Incomplete docs");
    }


    [Fact]
    public async Task PromoteToAdminAsync_ShouldThrow_WhenAlreadyAdmin()
    {
        var userId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            Role = UserRole.Admin
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId))
                     .ReturnsAsync(user);

        var service = CreateService();

        Func<Task> act = async () =>
            await service.PromoteToAdminAsync(userId, Guid.NewGuid());

        await act.Should()
                 .ThrowAsync<InvalidOperationException>()
                 .WithMessage("User is already an admin");
    }

    [Fact]
    public async Task PromoteToAdminAsync_ShouldPromote_WhenNotAdmin()
    {
        var userId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            Role = UserRole.Vendor
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId))
                     .ReturnsAsync(user);

        _userRepoMock.Setup(r => r.UpdateAsync(user))
                     .Returns(Task.CompletedTask);

        _userRepoMock.Setup(r => r.SaveChangesAsync())
                     .ReturnsAsync(1);

        var service = CreateService();

        await service.PromoteToAdminAsync(userId, Guid.NewGuid());

        user.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task SoftDeleteUserAsync_ShouldThrow_WhenAlreadyDeleted()
    {
        var userId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            IsSoftDeleted = true
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId))
                     .ReturnsAsync(user);

        var service = CreateService();

        Func<Task> act = async () =>
            await service.SoftDeleteUserAsync(userId, Guid.NewGuid());

        await act.Should()
                 .ThrowAsync<InvalidOperationException>()
                 .WithMessage("User is already deleted");
    }

    [Fact]
    public async Task SoftDeleteUserAsync_ShouldSoftDeleteUser()
    {
        var userId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            IsSoftDeleted = false
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId))
                     .ReturnsAsync(user);

        _userRepoMock.Setup(r => r.UpdateAsync(user))
                     .Returns(Task.CompletedTask);

        _userRepoMock.Setup(r => r.SaveChangesAsync())
                     .ReturnsAsync(1);

        var service = CreateService();

        await service.SoftDeleteUserAsync(userId, Guid.NewGuid());

        user.IsSoftDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task UnlockUserAsync_ShouldThrow_WhenNotLocked()
    {
        var userId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            Status = UserStatus.Approved
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId))
                     .ReturnsAsync(user);

        var service = CreateService();

        Func<Task> act = async () =>
            await service.UnlockUserAsync(userId, Guid.NewGuid());

        await act.Should()
                 .ThrowAsync<InvalidOperationException>()
                 .WithMessage("User is not locked");
    }

    [Fact]
    public async Task UnlockUserAsync_ShouldUnlockUser()
    {
        var userId = Guid.NewGuid();

        var user = new User
        {
            Id = userId,
            Status = UserStatus.Locked,
            FailedLoginCount = 5
        };

        _userRepoMock.Setup(r => r.GetByIdAsync(userId))
                     .ReturnsAsync(user);

        _userRepoMock.Setup(r => r.UpdateAsync(user))
                     .Returns(Task.CompletedTask);

        _userRepoMock.Setup(r => r.SaveChangesAsync())
                     .ReturnsAsync(1);

        var service = CreateService();

        await service.UnlockUserAsync(userId, Guid.NewGuid());

        user.Status.Should().Be(UserStatus.Approved);
        user.FailedLoginCount.Should().Be(0);
    }
}