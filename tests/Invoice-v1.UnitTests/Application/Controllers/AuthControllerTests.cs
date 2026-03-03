using invoice_v1.src.Api.Controllers;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace invoice_v1.tests.Controllers
{
    public class AuthControllerTests
    {
        private readonly Mock<IAuthService> _mockAuthService;
        private readonly Mock<IRateLimitService> _mockRateLimitService;
        private readonly Mock<ILogger<AuthController>> _mockLogger;
        private readonly AuthController _sut;

        public AuthControllerTests()
        {
            _mockAuthService = new Mock<IAuthService>();
            _mockRateLimitService = new Mock<IRateLimitService>();
            _mockLogger = new Mock<ILogger<AuthController>>();
            _sut = new AuthController(
                _mockAuthService.Object,
                _mockRateLimitService.Object,
                _mockLogger.Object);

            // Set up a default HttpContext with a remote IP
            _sut.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            _sut.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region Signup
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Signup_ValidRequest_ReturnsOk()
        {
            // Arrange
            var request = new SignupRequest
            {
                Email = "vendor@test.com",
                Password = "Str0ng!Pass",
                CompanyName = "Test Corp"
            };

            _mockRateLimitService.Setup(s => s.IsRateLimitedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(false);

            _mockAuthService.Setup(s => s.SignupAsync(request))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _sut.Signup(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockAuthService.Verify(s => s.SignupAsync(request), Times.Once);
        }

        [Fact]
        public async Task Signup_MissingEmail_ReturnsBadRequest()
        {
            // Arrange
            var request = new SignupRequest
            {
                Email = "",
                Password = "Str0ng!Pass",
                CompanyName = "Test Corp"
            };

            // Act
            var result = await _sut.Signup(request);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Signup_MissingCompanyName_ReturnsBadRequest()
        {
            // Arrange
            var request = new SignupRequest
            {
                Email = "vendor@test.com",
                Password = "Str0ng!Pass",
                CompanyName = ""
            };

            // Act
            var result = await _sut.Signup(request);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Signup_RateLimited_Returns429()
        {
            // Arrange
            var request = new SignupRequest
            {
                Email = "vendor@test.com",
                Password = "Str0ng!Pass",
                CompanyName = "Test Corp"
            };

            _mockRateLimitService.Setup(s => s.IsRateLimitedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);

            _mockRateLimitService.Setup(s => s.GetAttemptsAsync(It.IsAny<string>()))
                .ReturnsAsync(5);

            // Act
            var result = await _sut.Signup(request);

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(429, statusResult.StatusCode);
        }

        [Fact]
        public async Task Signup_DuplicateEmail_ReturnsBadRequest()
        {
            // Arrange
            var request = new SignupRequest
            {
                Email = "existing@test.com",
                Password = "Str0ng!Pass",
                CompanyName = "Test Corp"
            };

            _mockRateLimitService.Setup(s => s.IsRateLimitedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(false);

            _mockAuthService.Setup(s => s.SignupAsync(request))
                .ThrowsAsync(new InvalidOperationException("Email already in use"));

            // Act
            var result = await _sut.Signup(request);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region Login
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Login_ValidCredentials_ReturnsOk()
        {
            // Arrange
            var request = new LoginRequest { Email = "vendor@test.com", Password = "pass123" };
            var loginResult = new LoginResult { AccessToken = "jwt-token" };

            _mockRateLimitService.Setup(s => s.IsRateLimitedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(false);

            _mockAuthService.Setup(s => s.LoginAsync(request))
                .ReturnsAsync(loginResult);

            // Act
            var result = await _sut.Login(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(loginResult, okResult.Value);
            _mockRateLimitService.Verify(s => s.ResetAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Login_MissingFields_ReturnsBadRequest()
        {
            // Arrange
            var request = new LoginRequest { Email = "", Password = "" };

            // Act
            var result = await _sut.Login(request);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Login_InvalidCredentials_ReturnsUnauthorized()
        {
            // Arrange
            var request = new LoginRequest { Email = "vendor@test.com", Password = "wrong" };

            _mockRateLimitService.Setup(s => s.IsRateLimitedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(false);

            _mockAuthService.Setup(s => s.LoginAsync(request))
                .ThrowsAsync(new UnauthorizedAccessException("Invalid credentials"));

            _mockRateLimitService.Setup(s => s.GetAttemptsAsync(It.IsAny<string>()))
                .ReturnsAsync(1);

            // Act
            var result = await _sut.Login(request);

            // Assert
            Assert.IsType<UnauthorizedObjectResult>(result);
            _mockRateLimitService.Verify(s => s.IncrementAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);
        }

        [Fact]
        public async Task Login_AccountLocked_Returns429()
        {
            // Arrange
            var request = new LoginRequest { Email = "vendor@test.com", Password = "pass123" };

            _mockRateLimitService.Setup(s => s.IsRateLimitedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);

            _mockRateLimitService.Setup(s => s.GetAttemptsAsync(It.IsAny<string>()))
                .ReturnsAsync(5);

            // Act
            var result = await _sut.Login(request);

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(429, statusResult.StatusCode);
        }

        [Fact]
        public async Task Login_ServerError_Returns500()
        {
            // Arrange
            var request = new LoginRequest { Email = "vendor@test.com", Password = "pass123" };

            _mockRateLimitService.Setup(s => s.IsRateLimitedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(false);

            _mockAuthService.Setup(s => s.LoginAsync(request))
                .ThrowsAsync(new Exception("Database connection failed"));

            // Act
            var result = await _sut.Login(request);

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion
    }
}
