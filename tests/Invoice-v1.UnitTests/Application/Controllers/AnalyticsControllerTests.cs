using invoice_v1.src.Api.Controllers;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace invoice_v1.tests.Controllers
{
    public class AnalyticsControllerTests : ControllerTestBase
    {
        private readonly Mock<IAnalyticsService> _mockAnalyticsService;
        private readonly Mock<ILogger<AnalyticsController>> _mockLogger;
        private readonly AnalyticsController _sut;
        private readonly Guid _testVendorId = Guid.NewGuid();

        public AnalyticsControllerTests()
        {
            _mockAnalyticsService = new Mock<IAnalyticsService>();
            _mockLogger = new Mock<ILogger<AnalyticsController>>();
            _sut = new AnalyticsController(_mockAnalyticsService.Object, _mockLogger.Object);
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region GetProductSales
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetProductSales_ValidRange_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;
            var expected = new List<ProductSalesDto> { new() { ProductName = "Widget" } };

            _mockAnalyticsService.Setup(s => s.GetProductSalesByDateRangeAsync(
                start, end, null, _testVendorId))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.GetProductSales(start, end);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(expected, okResult.Value);
        }

        [Fact]
        public async Task GetProductSales_StartAfterEnd_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var start = DateTime.UtcNow;
            var end = DateTime.UtcNow.AddDays(-30);

            // Act
            var result = await _sut.GetProductSales(start, end);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetProductSales_FutureEndDate_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow.AddDays(10);

            // Act
            var result = await _sut.GetProductSales(start, end);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetProductSales_AdminRole_PassesNullVendorId()
        {
            // Arrange
            SetupUser(_sut, Guid.NewGuid(), role: "Admin");
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;
            var expected = new List<ProductSalesDto>();

            _mockAnalyticsService.Setup(s => s.GetProductSalesByDateRangeAsync(
                start, end, null, null))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.GetProductSales(start, end);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockAnalyticsService.Verify(s => s.GetProductSalesByDateRangeAsync(
                start, end, null, null), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetTrendingProducts
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetTrendingProducts_ValidParams_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;
            var expected = new List<ProductTrendDto> { new() };

            _mockAnalyticsService.Setup(s => s.GetTrendingProductsAsync(
                start, end, 5, _testVendorId))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.GetTrendingProducts(start, end, 5);

            // Assert
            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetTrendingProducts_TopNLessThanOne_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;

            // Act
            var result = await _sut.GetTrendingProducts(start, end, 0);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetTrendingProducts_TopNExceeds100_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;

            // Act
            var result = await _sut.GetTrendingProducts(start, end, 101);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetCategorySales
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetCategorySales_ValidRange_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;
            var expected = new List<CategorySalesDto> { new() };

            _mockAnalyticsService.Setup(s => s.GetCategorySalesAsync(start, end, _testVendorId))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.GetCategorySales(start, end);

            // Assert
            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetProductTimeSeries
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetProductTimeSeries_EmptyProductId_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;

            // Act
            var result = await _sut.GetProductTimeSeries("", start, end);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetProductTimeSeries_ValidParams_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;
            var expected = new List<ProductTimeSeriesDto> { new() };

            _mockAnalyticsService.Setup(s => s.GetProductTimeSeriesAsync(
                "PROD-1", start, end, TimeGranularity.Monthly, _testVendorId))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.GetProductTimeSeries("PROD-1", start, end);

            // Assert
            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetRevenueTrend
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetRevenueTrend_ValidRange_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;
            var expected = new List<RevenueTrendDto> { new() };

            _mockAnalyticsService.Setup(s => s.GetRevenueTrendAsync(
                start, end, TimeGranularity.Monthly, _testVendorId))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.GetRevenueTrend(start, end);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(expected, okResult.Value);
        }

        [Fact]
        public async Task GetRevenueTrend_StartDateAfterEndDate_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var start = DateTime.UtcNow;
            var end = DateTime.UtcNow.AddDays(-30);

            // Act
            var result = await _sut.GetRevenueTrend(start, end);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetRevenueTrend_AdminRole_UsesVendorIdParam()
        {
            // Arrange
            var customVendorId = Guid.NewGuid();
            SetupUser(_sut, Guid.NewGuid(), role: "Admin");
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;

            _mockAnalyticsService.Setup(s => s.GetRevenueTrendAsync(
                start, end, TimeGranularity.Monthly, customVendorId))
                .ReturnsAsync(new List<RevenueTrendDto>());

            // Act
            await _sut.GetRevenueTrend(start, end, TimeGranularity.Monthly, customVendorId);

            // Assert
            _mockAnalyticsService.Verify(s => s.GetRevenueTrendAsync(
                start, end, TimeGranularity.Monthly, customVendorId), Times.Once);
        }

        #endregion
    }
}
