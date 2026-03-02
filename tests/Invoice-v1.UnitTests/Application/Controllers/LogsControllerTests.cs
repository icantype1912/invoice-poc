using invoice_v1.src.Api.Controllers;
using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace invoice_v1.tests.Controllers
{
    public class LogsControllerTests : ControllerTestBase
    {
        private readonly Mock<IFileChangeLogService> _mockLogService;
        private readonly Mock<ILogger<LogsController>> _mockLogger;
        private readonly LogsController _sut;
        private readonly Guid _testVendorId = Guid.NewGuid();

        public LogsControllerTests()
        {
            _mockLogService = new Mock<IFileChangeLogService>();
            _mockLogger = new Mock<ILogger<LogsController>>();
            _sut = new LogsController(_mockLogService.Object, _mockLogger.Object);
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region GetLogs
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetLogs_AsVendor_PassesVendorIdAndFilters()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var expectedResult = new { Data = new List<object>(), Total = 0 };

            _mockLogService.Setup(s => s.GetLogsAsync(_testVendorId, "Upload", 1, 50))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _sut.GetLogs(1, 50, "Upload");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(expectedResult, okResult.Value);
            _mockLogService.Verify(s => s.GetLogsAsync(_testVendorId, "Upload", 1, 50), Times.Once);
        }

        [Fact]
        public async Task GetLogs_AsAdmin_PassesNullVendorId()
        {
            // Arrange
            SetupUser(_sut, Guid.NewGuid(), role: "Admin");
            var expectedResult = new { Data = new List<object>(), Total = 100 };

            _mockLogService.Setup(s => s.GetLogsAsync(null, null, 2, 20))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _sut.GetLogs(2, 20, null);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockLogService.Verify(s => s.GetLogsAsync(null, null, 2, 20), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetLogById
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetLogById_Exists_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var logId = 123;
            var logObject = new { Id = logId, FileName = "test.pdf" };

            _mockLogService.Setup(s => s.GetLogByIdAsync(logId, _testVendorId))
                .ReturnsAsync(logObject);

            // Act
            var result = await _sut.GetLogById(logId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(logObject, okResult.Value);
        }

        [Fact]
        public async Task GetLogById_NotFound_ReturnsNotFound()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var logId = 999;

            _mockLogService.Setup(s => s.GetLogByIdAsync(logId, _testVendorId))
                .ReturnsAsync((object?)null);

            // Act
            var result = await _sut.GetLogById(logId);

            // Assert
            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            // Verify error message content
            var errorProp = notFoundResult.Value?.GetType().GetProperty("error");
            Assert.Contains(logId.ToString(), errorProp?.GetValue(notFoundResult.Value)?.ToString() ?? "");
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetLogStats
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetLogStats_ReturnsStatsFromService()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var statsObject = new { totalFiles = 10, totalProcessed = 8 };

            _mockLogService.Setup(s => s.GetLogStatsAsync(_testVendorId))
                .ReturnsAsync(statsObject);

            // Act
            var result = await _sut.GetLogStats();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(statsObject, okResult.Value);
            _mockLogService.Verify(s => s.GetLogStatsAsync(_testVendorId), Times.Once);
        }

        #endregion
    }
}
