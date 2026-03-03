using invoice_v1.src.Api.Controllers;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace invoice_v1.tests.Controllers
{
    public class SearchControllerTests
    {
        private readonly Mock<ISearchService> _mockSearchService;
        private readonly Mock<ILogger<SearchController>> _mockLogger;
        private readonly SearchController _sut;

        public SearchControllerTests()
        {
            _mockSearchService = new Mock<ISearchService>();
            _mockLogger = new Mock<ILogger<SearchController>>();
            _sut = new SearchController(_mockSearchService.Object, _mockLogger.Object);
        }

        private void SetupVendor(Guid vendorId)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, vendorId.ToString()),
                new(ClaimTypes.Role, "Vendor")
            };
            _sut.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            };
        }

        private void SetupAdmin(Guid adminId)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, adminId.ToString()),
                new(ClaimTypes.Role, "Admin")
            };
            _sut.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            };
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region Search
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Search_EmptyQuery_ReturnsBadRequest()
        {
            // Arrange
            SetupVendor(Guid.NewGuid());
            var request = new SearchRequest { Query = "" };

            // Act
            var result = await _sut.Search(request);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Search_VendorQuery_PassesVendorId()
        {
            // Arrange
            var vendorId = Guid.NewGuid();
            SetupVendor(vendorId);
            var request = new SearchRequest { Query = "show my invoices" };
            var expected = new SearchResultDto { RowCount = 5 };

            _mockSearchService.Setup(s => s.SearchAsync(
                "show my invoices", vendorId, vendorId.ToString()))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.Search(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(expected, okResult.Value);
        }

        [Fact]
        public async Task Search_AdminQuery_PassesNullVendorId()
        {
            // Arrange
            var adminId = Guid.NewGuid();
            SetupAdmin(adminId);
            var request = new SearchRequest { Query = "show all invoices" };
            var expected = new SearchResultDto { RowCount = 100 };

            _mockSearchService.Setup(s => s.SearchAsync(
                "show all invoices", null, adminId.ToString()))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.Search(request);

            // Assert
            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task Search_RateLimited_Returns429()
        {
            // Arrange
            var vendorId = Guid.NewGuid();
            SetupVendor(vendorId);
            var request = new SearchRequest { Query = "show my invoices" };
            var rateLimitResult = new SearchResultDto { Error = "Too many requests" };

            _mockSearchService.Setup(s => s.SearchAsync(
                "show my invoices", vendorId, vendorId.ToString()))
                .ReturnsAsync(rateLimitResult);

            // Act
            var result = await _sut.Search(request);

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(429, statusResult.StatusCode);
        }

        [Fact]
        public async Task Search_VendorWithInvalidId_ReturnsUnauthorized()
        {
            // Arrange — Vendor with an invalid (non-GUID) user id
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "not-a-guid"),
                new(ClaimTypes.Role, "Vendor")
            };
            _sut.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            };
            var request = new SearchRequest { Query = "show my invoices" };

            // Act
            var result = await _sut.Search(request);

            // Assert
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Search_SuccessResult_ReturnsOk()
        {
            // Arrange
            var vendorId = Guid.NewGuid();
            SetupVendor(vendorId);
            var request = new SearchRequest { Query = "total revenue" };
            var expected = new SearchResultDto
            {
                NaturalLanguageQuery = "total revenue",
                GeneratedSql = "SELECT SUM(amount) FROM invoices LIMIT 10",
                RowCount = 1,
                Error = null
            };

            _mockSearchService.Setup(s => s.SearchAsync(
                "total revenue", vendorId, vendorId.ToString()))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.Search(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<SearchResultDto>(okResult.Value);
            Assert.Equal(1, dto.RowCount);
            Assert.Null(dto.Error);
        }

        #endregion
    }
}
