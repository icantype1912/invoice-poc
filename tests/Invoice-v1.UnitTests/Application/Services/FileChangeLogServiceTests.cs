using invoice_v1.src.Application.Services;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Repositories;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Services;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace invoice_v1.tests.Application.Services
{
    public class FileChangeLogServiceTests
    {
        private readonly Mock<IFileChangeLogRepository> _mockRepo;
        private readonly Mock<ILogger<FileChangeLogService>> _mockLogger;
        private readonly FileChangeLogService _sut;

        public FileChangeLogServiceTests()
        {
            _mockRepo = new Mock<IFileChangeLogRepository>();
            _mockLogger = new Mock<ILogger<FileChangeLogService>>();
            _sut = new FileChangeLogService(_mockRepo.Object, _mockLogger.Object);
        }

        private static T Prop<T>(object obj, string name) =>
            (T)obj.GetType().GetProperty(name)!.GetValue(obj)!;

        private void VerifyLog(LogLevel level, string contains, Times times) =>
            _mockLogger.Verify(
                l => l.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(contains)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                times);

        private void SetupEmptyLogs(Guid? vendorId, string? changeType, int skip, int take)
        {
            _mockRepo.Setup(r => r.GetLogCountAsync(vendorId, changeType)).ReturnsAsync(0);
            _mockRepo.Setup(r => r.GetLogsAsync(vendorId, changeType, skip, take))
                     .ReturnsAsync(new List<FileChangeLog>());
        }

        private void SetupStats(Guid? vendorId, List<(string, int, int, int)> stats, int total)
        {
            _mockRepo.Setup(r => r.GetLogStatsAsync(vendorId)).ReturnsAsync(stats);
            _mockRepo.Setup(r => r.GetLogCountAsync(vendorId, null)).ReturnsAsync(total);
        }

        #region GetLogsAsync — Normalization

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task GetLogsAsync_PageLessThan1_NormalizesTo1(int badPage)
        {
            SetupEmptyLogs(null, null, 0, 50);
            var result = await _sut.GetLogsAsync(null, null, badPage, 50);
            Assert.Equal(1, Prop<int>(result, "page"));
            _mockRepo.Verify(r => r.GetLogsAsync(null, null, 0, 50), Times.Once);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(101)]
        public async Task GetLogsAsync_InvalidPageSize_NormalizesTo50(int badSize)
        {
            SetupEmptyLogs(null, null, 0, 50);
            var result = await _sut.GetLogsAsync(null, null, 1, badSize);
            Assert.Equal(50, Prop<int>(result, "pageSize"));
        }

        [Fact]
        public async Task GetLogsAsync_ValidPageSize_IsRetained()
        {
            SetupEmptyLogs(null, null, 0, 25);
            var result = await _sut.GetLogsAsync(null, null, 1, 25);
            Assert.Equal(25, Prop<int>(result, "pageSize"));
        }

        #endregion

        #region GetLogsAsync — Skip & Pagination

        [Theory]
        [InlineData(1, 10, 0)]
        [InlineData(2, 10, 10)]
        [InlineData(3, 10, 20)]
        [InlineData(5, 20, 80)]
        public async Task GetLogsAsync_CalculatesSkipCorrectly(int page, int pageSize, int expectedSkip)
        {
            SetupEmptyLogs(null, null, expectedSkip, pageSize);
            await _sut.GetLogsAsync(null, null, page, pageSize);
            _mockRepo.Verify(r => r.GetLogsAsync(null, null, expectedSkip, pageSize), Times.Once);
        }

        [Fact]
        public async Task GetLogsAsync_TotalPagesUseCeiling()
        {
            _mockRepo.Setup(r => r.GetLogCountAsync(null, null)).ReturnsAsync(25);
            _mockRepo.Setup(r => r.GetLogsAsync(null, null, 0, 10)).ReturnsAsync(new List<FileChangeLog>());

            var result = await _sut.GetLogsAsync(null, null, 1, 10);

            Assert.Equal(25, Prop<int>(result, "total"));
            Assert.Equal(3, Prop<int>(result, "totalPages"));
        }

        [Fact]
        public async Task GetLogsAsync_TotalPagesExactDivision()
        {
            _mockRepo.Setup(r => r.GetLogCountAsync(null, null)).ReturnsAsync(20);
            _mockRepo.Setup(r => r.GetLogsAsync(null, null, 0, 10)).ReturnsAsync(new List<FileChangeLog>());

            var result = await _sut.GetLogsAsync(null, null, 1, 10);
            Assert.Equal(2, Prop<int>(result, "totalPages"));
        }

        #endregion

        #region GetLogsAsync — Repository Delegation & Mapping

        [Fact]
        public async Task GetLogsAsync_PassesVendorIdAndChangeTypeToRepository()
        {
            var vendorId = Guid.NewGuid();
            SetupEmptyLogs(vendorId, "ADDED", 0, 10);

            await _sut.GetLogsAsync(vendorId, "ADDED", 1, 10);

            _mockRepo.Verify(r => r.GetLogCountAsync(vendorId, "ADDED"), Times.Once);
            _mockRepo.Verify(r => r.GetLogsAsync(vendorId, "ADDED", 0, 10), Times.Once);
        }

        [Fact]
        public async Task GetLogsAsync_MapsLogFieldsCorrectly()
        {
            var vendorId = Guid.NewGuid();
            var detectedAt = DateTime.UtcNow;
            var logs = new List<FileChangeLog>
            {
                new()
                {
                    Id                 = 42,
                    FileName           = "invoice.pdf",
                    FileId             = "gd-file-id",
                    ChangeType         = "ADDED",
                    DetectedAt         = detectedAt,
                    MimeType           = "application/pdf",
                    FileSize           = 2048,
                    Processed          = true,
                    SecurityStatus     = "Healthy",
                    UploadedByVendorId = vendorId
                }
            };
            _mockRepo.Setup(r => r.GetLogCountAsync(null, null)).ReturnsAsync(1);
            _mockRepo.Setup(r => r.GetLogsAsync(null, null, 0, 50)).ReturnsAsync(logs);

            var result = await _sut.GetLogsAsync(null, null, 1, 50);
            var returned = Prop<IEnumerable<object>>(result, "logs").ToList();

            Assert.Single(returned);
            Assert.Equal(42, Prop<int>(returned[0], "Id"));
            Assert.Equal("invoice.pdf", Prop<string>(returned[0], "FileName"));
            Assert.Equal("ADDED", Prop<string>(returned[0], "ChangeType"));
            Assert.Equal(detectedAt, Prop<DateTime>(returned[0], "DetectedAt"));
            Assert.Equal("Healthy", Prop<string>(returned[0], "SecurityStatus"));
            Assert.True(Prop<bool>(returned[0], "Processed"));
        }

        [Fact]
        public async Task GetLogsAsync_EmptyResult_ReturnsEmptyLogs()
        {
            SetupEmptyLogs(null, null, 0, 50);
            var result = await _sut.GetLogsAsync(null, null, 1, 50);
            Assert.Empty(Prop<IEnumerable<object>>(result, "logs"));
        }

        #endregion

        #region GetLogsAsync — Logging

        [Fact]
        public async Task GetLogsAsync_NullVendorId_LogsALL()
        {
            SetupEmptyLogs(null, null, 0, 50);
            await _sut.GetLogsAsync(null, null, 1, 50);
            VerifyLog(LogLevel.Information, "ALL", Times.Once());
        }

        [Fact]
        public async Task GetLogsAsync_WithVendorId_LogsVendorIdString()
        {
            var vendorId = Guid.NewGuid();
            SetupEmptyLogs(vendorId, null, 0, 50);
            await _sut.GetLogsAsync(vendorId, null, 1, 50);
            VerifyLog(LogLevel.Information, vendorId.ToString(), Times.Once());
        }

        #endregion

        #region GetLogByIdAsync

        [Fact]
        public async Task GetLogByIdAsync_LogNotFound_ReturnsNull()
        {
            _mockRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((FileChangeLog?)null);
            var result = await _sut.GetLogByIdAsync(99, null);
            Assert.Null(result);
        }

        [Fact]
        public async Task GetLogByIdAsync_VendorMismatch_ReturnsNull()
        {
            var ownerId = Guid.NewGuid();
            var requestId = Guid.NewGuid();
            _mockRepo.Setup(r => r.GetByIdAsync(1))
                     .ReturnsAsync(new FileChangeLog { Id = 1, UploadedByVendorId = ownerId });

            var result = await _sut.GetLogByIdAsync(1, requestId);
            Assert.Null(result);
        }

        [Fact]
        public async Task GetLogByIdAsync_VendorMismatch_LogsWarning()
        {
            var ownerId = Guid.NewGuid();
            var requestId = Guid.NewGuid();
            _mockRepo.Setup(r => r.GetByIdAsync(1))
                     .ReturnsAsync(new FileChangeLog { Id = 1, UploadedByVendorId = ownerId });

            await _sut.GetLogByIdAsync(1, requestId);
            VerifyLog(LogLevel.Warning, requestId.ToString(), Times.Once());
        }

        [Fact]
        public async Task GetLogByIdAsync_VendorMatch_ReturnsLog()
        {
            var vendorId = Guid.NewGuid();
            var log = new FileChangeLog
            {
                Id = 5,
                FileName = "receipt.pdf",
                UploadedByVendorId = vendorId,
                ChangeType = "MODIFIED",
                SecurityStatus = "Healthy"
            };
            _mockRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(log);

            var result = await _sut.GetLogByIdAsync(5, vendorId);

            Assert.NotNull(result);
            Assert.Equal(5, Prop<int>(result!, "Id"));
            Assert.Equal("receipt.pdf", Prop<string>(result!, "FileName"));
            Assert.Equal("MODIFIED", Prop<string>(result!, "ChangeType"));
        }

        [Fact]
        public async Task GetLogByIdAsync_NullVendorId_AdminBypass_ReturnsLog()
        {
            var log = new FileChangeLog { Id = 3, FileName = "admin.pdf" };
            _mockRepo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(log);

            var result = await _sut.GetLogByIdAsync(3, null);

            Assert.NotNull(result);
            Assert.Equal(3, Prop<int>(result!, "Id"));
        }

        [Fact]
        public async Task GetLogByIdAsync_VendorMatch_DoesNotLogWarning()
        {
            var vendorId = Guid.NewGuid();
            _mockRepo.Setup(r => r.GetByIdAsync(7))
                     .ReturnsAsync(new FileChangeLog { Id = 7, UploadedByVendorId = vendorId });

            await _sut.GetLogByIdAsync(7, vendorId);
            VerifyLog(LogLevel.Warning, string.Empty, Times.Never());
        }

        [Fact]
        public async Task GetLogByIdAsync_NotFound_DoesNotLogWarning()
        {
            _mockRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((FileChangeLog?)null);
            await _sut.GetLogByIdAsync(999, Guid.NewGuid());
            VerifyLog(LogLevel.Warning, string.Empty, Times.Never());
        }

        #endregion

        #region GetLogStatsAsync

        [Fact]
        public async Task GetLogStatsAsync_ReturnsTotalFilesFromCount()
        {
            SetupStats(null, new List<(string, int, int, int)>(), total: 15);
            var result = await _sut.GetLogStatsAsync(null);
            Assert.Equal(15, Prop<int>(result, "totalFiles"));
        }

        [Fact]
        public async Task GetLogStatsAsync_SumsTotalProcessed()
        {
            var stats = new List<(string, int, int, int)>
            {
                ("ADDED",    5, 3, 2),
                ("MODIFIED", 4, 2, 2),
                ("DELETED",  3, 1, 2)
            };
            SetupStats(null, stats, total: 12);

            var result = await _sut.GetLogStatsAsync(null);
            Assert.Equal(6, Prop<int>(result, "totalProcessed"));
        }

        [Fact]
        public async Task GetLogStatsAsync_TotalPendingIsTotalMinusProcessed()
        {
            var stats = new List<(string, int, int, int)> { ("ADDED", 10, 4, 6) };
            SetupStats(null, stats, total: 10);

            var result = await _sut.GetLogStatsAsync(null);
            Assert.Equal(4, Prop<int>(result, "totalProcessed"));
            Assert.Equal(6, Prop<int>(result, "totalPending"));
        }

        [Fact]
        public async Task GetLogStatsAsync_ByChangeType_MapsAllFields()
        {
            var stats = new List<(string, int, int, int)> { ("ADDED", 7, 5, 2) };
            SetupStats(null, stats, total: 7);

            var result = await _sut.GetLogStatsAsync(null);
            var breakdown = Prop<IEnumerable<object>>(result, "byChangeType").ToList();

            Assert.Single(breakdown);
            Assert.Equal("ADDED", Prop<string>(breakdown[0], "changeType"));
            Assert.Equal(7, Prop<int>(breakdown[0], "count"));
            Assert.Equal(5, Prop<int>(breakdown[0], "processed"));
            Assert.Equal(2, Prop<int>(breakdown[0], "pending"));
        }

        [Fact]
        public async Task GetLogStatsAsync_MultipleChangeTypes_AllMapped()
        {
            var stats = new List<(string, int, int, int)>
            {
                ("ADDED",    3, 2, 1),
                ("MODIFIED", 2, 1, 1)
            };
            SetupStats(null, stats, total: 5);

            var result = await _sut.GetLogStatsAsync(null);
            var breakdown = Prop<IEnumerable<object>>(result, "byChangeType").ToList();

            Assert.Equal(2, breakdown.Count);
            Assert.Contains(breakdown, b => Prop<string>(b, "changeType") == "ADDED");
            Assert.Contains(breakdown, b => Prop<string>(b, "changeType") == "MODIFIED");
        }

        [Fact]
        public async Task GetLogStatsAsync_PassesVendorIdToRepository()
        {
            var vendorId = Guid.NewGuid();
            SetupStats(vendorId, new List<(string, int, int, int)>(), total: 0);

            await _sut.GetLogStatsAsync(vendorId);

            _mockRepo.Verify(r => r.GetLogStatsAsync(vendorId), Times.Once);
            _mockRepo.Verify(r => r.GetLogCountAsync(vendorId, null), Times.Once);
        }

        [Fact]
        public async Task GetLogStatsAsync_EmptyStats_ReturnsZeroTotals()
        {
            SetupStats(null, new List<(string, int, int, int)>(), total: 0);

            var result = await _sut.GetLogStatsAsync(null);

            Assert.Equal(0, Prop<int>(result, "totalFiles"));
            Assert.Equal(0, Prop<int>(result, "totalProcessed"));
            Assert.Equal(0, Prop<int>(result, "totalPending"));
            Assert.Empty(Prop<IEnumerable<object>>(result, "byChangeType"));
        }

        #endregion
    }
}
