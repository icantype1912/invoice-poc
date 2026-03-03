using invoice_v1.src.Api.Controllers;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace invoice_v1.tests.Controllers
{
    public class AdminControllerTests : ControllerTestBase
    {
        private readonly Mock<IAdminUserService> _mockAdminService;
        private readonly AdminController _sut;
        private readonly Guid _adminId = Guid.NewGuid();

        public AdminControllerTests()
        {
            _mockAdminService = new Mock<IAdminUserService>();
            _sut = new AdminController(_mockAdminService.Object);
            SetupUser(_sut, _adminId, role: "Admin");
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region GetPendingUsers
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetPendingUsers_ReturnsOkWithUsers()
        {
            // Arrange
            var users = new List<User> { new() { Id = Guid.NewGuid(), Email = "test@test.com" } };
            _mockAdminService.Setup(s => s.GetPendingUsersAsync()).ReturnsAsync(users);

            // Act
            var result = await _sut.GetPendingUsers();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(users, okResult.Value);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetAllUsers
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetAllUsers_ReturnsOkWithUsers()
        {
            // Arrange
            var users = new List<User> { new(), new() };
            _mockAdminService.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(users);

            // Act
            var result = await _sut.GetAllUsers();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(users, okResult.Value);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region ApproveUser
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ApproveUser_ValidId_ReturnsOk()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _mockAdminService.Setup(s => s.ApproveUserAsync(userId, _adminId))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.ApproveUser(userId);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockAdminService.Verify(s => s.ApproveUserAsync(userId, _adminId), Times.Once);
        }

        [Fact]
        public async Task ApproveUser_ServiceThrows_Propagates()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _mockAdminService.Setup(s => s.ApproveUserAsync(userId, _adminId))
                .ThrowsAsync(new InvalidOperationException("User not found"));

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.ApproveUser(userId));
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region RejectUser
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task RejectUser_WithReason_ReturnsOk()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var request = new RejectUserRequest { Reason = "Incomplete documentation" };
            _mockAdminService.Setup(s => s.RejectUserAsync(userId, _adminId, request.Reason))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.RejectUser(userId, request);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockAdminService.Verify(s => s.RejectUserAsync(userId, _adminId, "Incomplete documentation"), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region PromoteUser
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task PromoteUser_ValidId_ReturnsOk()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _mockAdminService.Setup(s => s.PromoteToAdminAsync(userId, _adminId))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.PromoteUser(userId);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockAdminService.Verify(s => s.PromoteToAdminAsync(userId, _adminId), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region DeleteUser
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task DeleteUser_ValidId_ReturnsOk()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _mockAdminService.Setup(s => s.SoftDeleteUserAsync(userId, _adminId))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.DeleteUser(userId);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockAdminService.Verify(s => s.SoftDeleteUserAsync(userId, _adminId), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region UnlockUser
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task UnlockUser_ValidId_ReturnsOk()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _mockAdminService.Setup(s => s.UnlockUserAsync(userId, _adminId))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.UnlockUser(userId);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockAdminService.Verify(s => s.UnlockUserAsync(userId, _adminId), Times.Once);
        }

        [Fact]
        public async Task UnlockUser_ServiceThrows_Propagates()
        {
            // Arrange
            var userId = Guid.NewGuid();
            _mockAdminService.Setup(s => s.UnlockUserAsync(userId, _adminId))
                .ThrowsAsync(new InvalidOperationException("User not locked"));

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.UnlockUser(userId));
        }

        #endregion
    }
}
