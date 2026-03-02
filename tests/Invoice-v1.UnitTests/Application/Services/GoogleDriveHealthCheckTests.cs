using Google;
using Google.Apis.Requests; // Required for RequestError
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using GoogleFile = Google.Apis.Drive.v3.Data.File;

namespace invoice_v1.tests.Services
{
    public class GoogleDriveHealthCheckTests
    {
        private readonly Mock<IGoogleDriveService> _mockDriveService;
        private readonly Mock<IConfiguration> _mockConfig;
        private readonly Mock<ILogger<GoogleDriveHealthCheck>> _mockLogger;
        private readonly GoogleDriveHealthCheck _sut;

        public GoogleDriveHealthCheckTests()
        {
            _mockDriveService = new Mock<IGoogleDriveService>();
            _mockConfig = new Mock<IConfiguration>();
            _mockLogger = new Mock<ILogger<GoogleDriveHealthCheck>>();

            _sut = new GoogleDriveHealthCheck(
                _mockDriveService.Object,
                _mockConfig.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task CheckHealthAsync_WhenHealthy_ReturnsHealthyResult()
        {
            // Arrange
            _mockConfig.Setup(c => c["GoogleDrive:SharedFolderId"]).Returns("folder-123");
            _mockDriveService.Setup(s => s.ListAllFilesRecursivelyAsync("folder-123", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<GoogleFile> { new() { Id = "file1" }, new() { Id = "file2" } });

            // Act
            var result = await _sut.CheckHealthAsync(new HealthCheckContext());

            // Assert
            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Contains("Found 2 files", result.Description);
        }

        [Fact]
        public async Task CheckHealthAsync_MissingConfig_ReturnsDegradedResult()
        {
            // Arrange
            _mockConfig.Setup(c => c["GoogleDrive:SharedFolderId"]).Returns(""); // Missing

            // Act
            var result = await _sut.CheckHealthAsync(new HealthCheckContext());

            // Assert
            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Contains("not configured", result.Description);
        }

        [Fact]
        public async Task CheckHealthAsync_GoogleApiError_ReturnsUnhealthyResult()
        {
            // Arrange
            _mockConfig.Setup(c => c["GoogleDrive:SharedFolderId"]).Returns("folder-123");

            // FIX: Manually populate the RequestError object. 
            // In the Google SDK, the top-level message and the Error.Message are separate.
            var apiException = new GoogleApiException("GoogleDrive", "Permission denied")
            {
                Error = new RequestError
                {
                    Message = "Permission denied",
                    Code = 403
                }
            };

            _mockDriveService.Setup(s => s.ListAllFilesRecursivelyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(apiException);

            // Act
            var result = await _sut.CheckHealthAsync(new HealthCheckContext());

            // Assert
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            // Now the service will extract "Permission denied" from gex.Error.Message
            Assert.Contains("Permission denied", result.Description);

            _mockLogger.Verify(
                x => x.Log(LogLevel.Error, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Google Drive API error")),
                    It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task CheckHealthAsync_GeneralException_ReturnsUnhealthyResult()
        {
            // Arrange
            _mockConfig.Setup(c => c["GoogleDrive:SharedFolderId"]).Returns("folder-123");
            _mockDriveService.Setup(s => s.ListAllFilesRecursivelyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Network timeout"));

            // Act
            var result = await _sut.CheckHealthAsync(new HealthCheckContext());

            // Assert
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Equal("Google Drive service is not accessible. Check service account configuration.", result.Description);
        }
    }
}
