using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Services;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace invoice_v1.tests.Services
{
    public class AnalyticsServiceTests
    {
        private readonly Mock<IAnalyticsRepository> _mockRepo;
        private readonly Mock<ILogger<AnalyticsService>> _mockLogger;
        private readonly AnalyticsService _sut;

        public AnalyticsServiceTests()
        {
            _mockRepo = new Mock<IAnalyticsRepository>();
            _mockLogger = new Mock<ILogger<AnalyticsService>>();
            _sut = new AnalyticsService(_mockRepo.Object, _mockLogger.Object);
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region Delegation Tests
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetProductSalesByDateRangeAsync_DelegatesToRepo()
        {
            // Arrange
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;
            var vendorId = Guid.NewGuid();
            var expected = new List<ProductSalesDto> { new() { ProductName = "Widget" } };

            _mockRepo.Setup(r => r.GetProductSalesByDateRangeAsync(start, end, "Office", vendorId))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.GetProductSalesByDateRangeAsync(start, end, "Office", vendorId);

            // Assert
            Assert.Equal(expected, result);
            _mockRepo.Verify(r => r.GetProductSalesByDateRangeAsync(start, end, "Office", vendorId), Times.Once);
        }

        [Fact]
        public async Task GetTrendingProductsAsync_DelegatesToRepo()
        {
            // Arrange
            var start = DateTime.UtcNow.AddDays(-90);
            var end = DateTime.UtcNow;
            var expected = new List<ProductTrendDto> { new() { ProductName = "Gadget", Rank = 1 } };

            _mockRepo.Setup(r => r.GetTrendingProductsAsync(start, end, 5, null))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.GetTrendingProductsAsync(start, end, 5);

            // Assert
            Assert.Single(result);
            Assert.Equal(1, result[0].Rank);
        }

        [Fact]
        public async Task GetCategorySalesAsync_DelegatesToRepo()
        {
            // Arrange
            var start = DateTime.UtcNow.AddDays(-7);
            var end = DateTime.UtcNow;
            var expected = new List<CategorySalesDto>
            {
                new() { Category = "Electronics", TotalRevenue = 5000m },
                new() { Category = "Office", TotalRevenue = 2000m }
            };

            _mockRepo.Setup(r => r.GetCategorySalesAsync(start, end, null))
                .ReturnsAsync(expected);

            // Act
            var result = await _sut.GetCategorySalesAsync(start, end);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal("Electronics", result[0].Category);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region AggregateTimeSeries
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetProductTimeSeriesAsync_MonthlyGranularity_GroupsByMonth()
        {
            // Arrange
            var invoiceId1 = Guid.NewGuid();
            var invoiceId2 = Guid.NewGuid();
            var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2025, 3, 31, 0, 0, 0, DateTimeKind.Utc);

            var rawData = new List<(DateTime, Guid, string, string, decimal, decimal)>
            {
                (new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc), invoiceId1, "P1", "Widget", 10m, 100m),
                (new DateTime(2025, 1, 20, 0, 0, 0, DateTimeKind.Utc), invoiceId2, "P1", "Widget", 5m, 50m),
                (new DateTime(2025, 2, 10, 0, 0, 0, DateTimeKind.Utc), invoiceId1, "P1", "Widget", 3m, 30m),
            };

            _mockRepo.Setup(r => r.GetProductTimeSeriesDataAsync("P1", start, end, null))
                .ReturnsAsync(rawData);

            // Act
            var result = await _sut.GetProductTimeSeriesAsync("P1", start, end, TimeGranularity.Monthly);

            // Assert
            Assert.Equal(2, result.Count); // Jan and Feb groups
            Assert.Equal(new DateTime(2025, 1, 1), result[0].Period);
            Assert.Equal(15m, result[0].Quantity); // 10 + 5
            Assert.Equal(150m, result[0].Revenue);  // 100 + 50
            Assert.Equal(2, result[0].InvoiceCount); // 2 distinct invoice IDs in Jan

            Assert.Equal(new DateTime(2025, 2, 1), result[1].Period);
            Assert.Equal(3m, result[1].Quantity);
            Assert.Equal(1, result[1].InvoiceCount); // 1 invoice in Feb
        }

        [Fact]
        public async Task GetProductTimeSeriesAsync_DistinctInvoiceCount_NotLineItemCount()
        {
            // Arrange — same invoice ID appearing multiple times should count as 1
            var invoiceId = Guid.NewGuid();
            var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2025, 1, 31, 0, 0, 0, DateTimeKind.Utc);

            var rawData = new List<(DateTime, Guid, string, string, decimal, decimal)>
            {
                (new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc), invoiceId, "P1", "Widget", 10m, 100m),
                (new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc), invoiceId, "P1", "Widget", 5m, 50m),
                (new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc), invoiceId, "P1", "Widget", 2m, 20m),
            };

            _mockRepo.Setup(r => r.GetProductTimeSeriesDataAsync("P1", start, end, null))
                .ReturnsAsync(rawData);

            // Act
            var result = await _sut.GetProductTimeSeriesAsync("P1", start, end, TimeGranularity.Monthly);

            // Assert
            Assert.Single(result);
            Assert.Equal(17m, result[0].Quantity); // 10 + 5 + 2
            Assert.Equal(1, result[0].InvoiceCount); // Same invoice ID → count = 1
        }

        [Fact]
        public async Task GetProductTimeSeriesAsync_EmptyData_ReturnsEmptyList()
        {
            // Arrange
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;
            var rawData = new List<(DateTime, Guid, string, string, decimal, decimal)>();

            _mockRepo.Setup(r => r.GetProductTimeSeriesDataAsync("P1", start, end, null))
                .ReturnsAsync(rawData);

            // Act
            var result = await _sut.GetProductTimeSeriesAsync("P1", start, end, TimeGranularity.Monthly);

            // Assert
            Assert.Empty(result);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region RevenueTrend
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetRevenueTrendAsync_DailyGranularity_GroupsByDay()
        {
            // Arrange
            var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc);

            var rawData = new List<(DateTime, decimal)>
            {
                (new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc), 100m),
                (new DateTime(2025, 1, 1, 15, 0, 0, DateTimeKind.Utc), 150m),
                (new DateTime(2025, 1, 2, 09, 0, 0, DateTimeKind.Utc), 200m),
            };

            _mockRepo.Setup(r => r.GetRevenueTrendDataAsync(start, end, null))
                .ReturnsAsync(rawData);

            // Act
            var result = await _sut.GetRevenueTrendAsync(start, end, TimeGranularity.Daily);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal(new DateTime(2025, 1, 1), result[0].Period);
            Assert.Equal(250m, result[0].Revenue);
            Assert.Equal(2, result[0].InvoiceCount);

            Assert.Equal(new DateTime(2025, 1, 2), result[1].Period);
            Assert.Equal(200m, result[1].Revenue);
            Assert.Equal(1, result[1].InvoiceCount);
        }

        [Fact]
        public async Task GetRevenueTrendAsync_DelegatesToRepoWithVendorId()
        {
            // Arrange
            var start = DateTime.UtcNow.AddDays(-30);
            var end = DateTime.UtcNow;
            var vendorId = Guid.NewGuid();
            _mockRepo.Setup(r => r.GetRevenueTrendDataAsync(start, end, vendorId))
                .ReturnsAsync(new List<(DateTime, decimal)>());

            // Act
            await _sut.GetRevenueTrendAsync(start, end, TimeGranularity.Monthly, vendorId);

            // Assert
            _mockRepo.Verify(r => r.GetRevenueTrendDataAsync(start, end, vendorId), Times.Once);
        }

        #endregion
    }
}
