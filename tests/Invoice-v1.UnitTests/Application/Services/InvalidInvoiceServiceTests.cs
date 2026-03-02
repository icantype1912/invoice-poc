using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Services;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace invoice_v1.tests.Application.Services
{
    public class InvalidInvoiceServiceTests
    {
        private readonly Mock<IInvalidInvoiceRepository> _mockInvalidRepo;
        private readonly Mock<IFileChangeLogRepository> _mockFileLogRepo;
        private readonly Mock<ILogger<InvalidInvoiceService>> _mockLogger;
        private readonly InvalidInvoiceService _sut;

        public InvalidInvoiceServiceTests()
        {
            _mockInvalidRepo = new Mock<IInvalidInvoiceRepository>();
            _mockFileLogRepo = new Mock<IFileChangeLogRepository>();
            _mockLogger = new Mock<ILogger<InvalidInvoiceService>>();

            _sut = new InvalidInvoiceService(
                _mockInvalidRepo.Object,
                _mockFileLogRepo.Object,
                _mockLogger.Object);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────

        private static T Prop<T>(object obj, string name) =>
            (T)obj.GetType().GetProperty(name)!.GetValue(obj)!;

        private static InvalidInvoice BuildInvalidInvoice(
            Guid? jobId = null,
            string? fileId = "file-001",
            string? fileName = "invoice.pdf",
            DateTime? created = null) => new()
            {
                Id = Guid.NewGuid(),
                FileId = fileId,
                FileName = fileName,
                VendorId = null,
                JobId = jobId ?? Guid.NewGuid(),
                Reason = JsonSerializer.SerializeToDocument(new { error = "parse failed" }),
                CreatedAt = created ?? DateTime.UtcNow
            };

        private static FileChangeLog BuildUnhealthyLog(
            int id = 1,
            string? fileId = "file-sec-001",
            string? fileName = "virus.pdf",
            DateTime? created = null) => new()
            {
                Id = id,
                FileId = fileId,
                FileName = fileName,
                UploadedByVendorId = null,
                SecurityFailReason = "Virus detected",
                DetectedAt = created ?? DateTime.UtcNow
            };

        private void SetupRepos(
            List<InvalidInvoice>? invalidInvoices = null,
            int extractionTotal = 0,
            List<FileChangeLog>? securityLogs = null,
            int securityTotal = 0)
        {
            _mockInvalidRepo
                .Setup(r => r.GetInvalidInvoicesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>()))
                .ReturnsAsync((invalidInvoices ?? new List<InvalidInvoice>(), extractionTotal));

            _mockFileLogRepo
                .Setup(r => r.GetUnhealthyLogsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>()))
                .ReturnsAsync((securityLogs ?? new List<FileChangeLog>(), securityTotal));
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region Page & PageSize Normalization
        // ────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-99)]
        public async Task GetInvalidInvoices_PageLessThan1_NormalizesTo1(int badPage)
        {
            SetupRepos();
            await _sut.GetInvalidInvoicesAsync(badPage, 20, null);

            _mockInvalidRepo.Verify(r => r.GetInvalidInvoicesAsync(1, 20, null), Times.Once);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(101)]
        [InlineData(500)]
        public async Task GetInvalidInvoices_InvalidPageSize_NormalizesTo20(int badSize)
        {
            SetupRepos();
            var result = await _sut.GetInvalidInvoicesAsync(1, badSize, null);
            Assert.Equal(20, Prop<int>(result, "PageSize"));
        }

        [Fact]
        public async Task GetInvalidInvoices_ValidPageSize_IsRetained()
        {
            SetupRepos();
            var result = await _sut.GetInvalidInvoicesAsync(1, 50, null);
            Assert.Equal(50, Prop<int>(result, "PageSize"));
        }

        [Fact]
        public async Task GetInvalidInvoices_PageSize100_IsRetained()
        {
            SetupRepos();
            var result = await _sut.GetInvalidInvoicesAsync(1, 100, null);
            Assert.Equal(100, Prop<int>(result, "PageSize"));
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region Repository Delegation
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetInvalidInvoices_PassesPageAndPageSizeToInvalidRepo()
        {
            SetupRepos();
            await _sut.GetInvalidInvoicesAsync(2, 15, null);
            _mockInvalidRepo.Verify(r => r.GetInvalidInvoicesAsync(2, 15, null), Times.Once);
        }

        [Fact]
        public async Task GetInvalidInvoices_PassesPageAndPageSizeToFileLogRepo()
        {
            SetupRepos();
            await _sut.GetInvalidInvoicesAsync(2, 15, null);
            _mockFileLogRepo.Verify(r => r.GetUnhealthyLogsAsync(2, 15, null), Times.Once);
        }

        [Fact]
        public async Task GetInvalidInvoices_PassesVendorIdToBothRepositories()
        {
            var vendorId = Guid.NewGuid();
            SetupRepos();

            await _sut.GetInvalidInvoicesAsync(1, 20, vendorId);

            _mockInvalidRepo.Verify(r => r.GetInvalidInvoicesAsync(1, 20, vendorId), Times.Once);
            _mockFileLogRepo.Verify(r => r.GetUnhealthyLogsAsync(1, 20, vendorId), Times.Once);
        }

        [Fact]
        public async Task GetInvalidInvoices_CallsBothReposExactlyOnce()
        {
            SetupRepos();
            await _sut.GetInvalidInvoicesAsync(1, 20, null);

            _mockInvalidRepo.Verify(r => r.GetInvalidInvoicesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>()), Times.Once);
            _mockFileLogRepo.Verify(r => r.GetUnhealthyLogsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>()), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region TotalCount & TotalPages
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetInvalidInvoices_TotalCountIsExtractionPlusSecurity()
        {
            SetupRepos(extractionTotal: 7, securityTotal: 3);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);

            Assert.Equal(10, Prop<int>(result, "TotalCount"));
        }

        [Fact]
        public async Task GetInvalidInvoices_TotalPagesUsesCeiling()
        {
            SetupRepos(extractionTotal: 11, securityTotal: 0);

            var result = await _sut.GetInvalidInvoicesAsync(1, 5, null);

            Assert.Equal(3, Prop<int>(result, "TotalPages")); // ceil(11/5) = 3
        }

        [Fact]
        public async Task GetInvalidInvoices_TotalPagesExactDivision()
        {
            SetupRepos(extractionTotal: 10, securityTotal: 0);

            var result = await _sut.GetInvalidInvoicesAsync(1, 5, null);

            Assert.Equal(2, Prop<int>(result, "TotalPages"));
        }

        [Fact]
        public async Task GetInvalidInvoices_BothEmpty_TotalCountZero()
        {
            SetupRepos();
            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            Assert.Equal(0, Prop<int>(result, "TotalCount"));
        }

        [Fact]
        public async Task GetInvalidInvoices_ReturnsCorrectPageMetadata()
        {
            SetupRepos(extractionTotal: 5, securityTotal: 5);

            var result = await _sut.GetInvalidInvoicesAsync(3, 10, null);

            Assert.Equal(3, Prop<int>(result, "Page"));
            Assert.Equal(10, Prop<int>(result, "PageSize"));
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region Extraction Failure Mapping
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetInvalidInvoices_ExtractionEntry_TypeIsExtractionFailure()
        {
            var invoice = BuildInvalidInvoice();
            SetupRepos(invalidInvoices: [invoice], extractionTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var data = Prop<IEnumerable<object>>(result, "Data").ToList();

            Assert.Equal("ExtractionFailure", Prop<string>(data[0], "Type"));
        }

        [Fact]
        public async Task GetInvalidInvoices_ExtractionEntry_IdIsGuidString()
        {
            var invoice = BuildInvalidInvoice();
            SetupRepos(invalidInvoices: [invoice], extractionTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var data = Prop<IEnumerable<object>>(result, "Data").ToList();

            Assert.Equal(invoice.Id.ToString(), Prop<string>(data[0], "Id"));
        }

        [Fact]
        public async Task GetInvalidInvoices_ExtractionEntry_MapsFileFields()
        {
            var invoice = BuildInvalidInvoice(fileId: "drive-xyz", fileName: "test.pdf");
            SetupRepos(invalidInvoices: [invoice], extractionTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var item = Prop<IEnumerable<object>>(result, "Data").First();

            Assert.Equal("drive-xyz", Prop<string?>(item, "FileId"));
            Assert.Equal("test.pdf", Prop<string?>(item, "FileName"));
        }

        [Fact]
        public async Task GetInvalidInvoices_ExtractionEntry_MapsJobId()
        {
            var jobId = Guid.NewGuid();
            var invoice = BuildInvalidInvoice(jobId: jobId);
            SetupRepos(invalidInvoices: [invoice], extractionTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var item = Prop<IEnumerable<object>>(result, "Data").First();

            Assert.Equal(jobId, Prop<Guid?>(item, "JobId"));
        }

        [Fact]
        public async Task GetInvalidInvoices_ExtractionEntry_MapsReasonFromJsonDocument()
        {
            var invoice = BuildInvalidInvoice();
            SetupRepos(invalidInvoices: [invoice], extractionTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var item = Prop<IEnumerable<object>>(result, "Data").First();

            Assert.NotNull(Prop<string?>(item, "Reason"));
        }

        [Fact]
        public async Task GetInvalidInvoices_ExtractionEntry_NullReason_MapsToNull()
        {
            var invoice = BuildInvalidInvoice();
            invoice.Reason = null;
            SetupRepos(invalidInvoices: [invoice], extractionTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var item = Prop<IEnumerable<object>>(result, "Data").First();

            Assert.Null(Prop<string?>(item, "Reason"));
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region Security Violation Mapping
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetInvalidInvoices_SecurityEntry_TypeIsSecurityViolation()
        {
            var log = BuildUnhealthyLog();
            SetupRepos(securityLogs: [log], securityTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var data = Prop<IEnumerable<object>>(result, "Data").ToList();

            Assert.Equal("SecurityViolation", Prop<string>(data[0], "Type"));
        }

        [Fact]
        public async Task GetInvalidInvoices_SecurityEntry_IdHasSecPrefix()
        {
            var log = BuildUnhealthyLog(id: 42);
            SetupRepos(securityLogs: [log], securityTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var item = Prop<IEnumerable<object>>(result, "Data").First();

            Assert.Equal("sec_42", Prop<string>(item, "Id"));
        }

        [Fact]
        public async Task GetInvalidInvoices_SecurityEntry_JobIdIsNull()
        {
            var log = BuildUnhealthyLog();
            SetupRepos(securityLogs: [log], securityTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var item = Prop<IEnumerable<object>>(result, "Data").First();

            Assert.Null(Prop<Guid?>(item, "JobId"));
        }

        [Fact]
        public async Task GetInvalidInvoices_SecurityEntry_MapsFileFields()
        {
            var log = BuildUnhealthyLog(fileId: "sec-file", fileName: "malware.exe");
            SetupRepos(securityLogs: [log], securityTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var item = Prop<IEnumerable<object>>(result, "Data").First();

            Assert.Equal("sec-file", Prop<string?>(item, "FileId"));
            Assert.Equal("malware.exe", Prop<string?>(item, "FileName"));
        }

        [Fact]
        public async Task GetInvalidInvoices_SecurityEntry_MapsSecurityFailReason()
        {
            var log = BuildUnhealthyLog();
            log.SecurityFailReason = "Trojan detected";
            SetupRepos(securityLogs: [log], securityTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var item = Prop<IEnumerable<object>>(result, "Data").First();

            Assert.Equal("Trojan detected", Prop<string?>(item, "Reason"));
        }

        [Fact]
        public async Task GetInvalidInvoices_SecurityEntry_MapsVendorIdFromUploadedByVendorId()
        {
            var vendorId = Guid.NewGuid();
            var log = BuildUnhealthyLog();
            log.UploadedByVendorId = vendorId;
            SetupRepos(securityLogs: [log], securityTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var item = Prop<IEnumerable<object>>(result, "Data").First();

            Assert.Equal(vendorId, Prop<Guid?>(item, "VendorId"));
        }

        [Fact]
        public async Task GetInvalidInvoices_SecurityEntry_CreatedAtMapsFromDetectedAt()
        {
            var detectedAt = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
            var log = BuildUnhealthyLog(created: detectedAt);
            SetupRepos(securityLogs: [log], securityTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var item = Prop<IEnumerable<object>>(result, "Data").First();

            Assert.Equal(detectedAt, Prop<DateTime>(item, "CreatedAt"));
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region Merge & Sort
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetInvalidInvoices_MergesBothSources()
        {
            SetupRepos(
                invalidInvoices: [BuildInvalidInvoice()],
                extractionTotal: 1,
                securityLogs: [BuildUnhealthyLog()],
                securityTotal: 1);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var data = Prop<IEnumerable<object>>(result, "Data").ToList();

            Assert.Equal(2, data.Count);
            Assert.Contains(data, d => Prop<string>(d, "Type") == "ExtractionFailure");
            Assert.Contains(data, d => Prop<string>(d, "Type") == "SecurityViolation");
        }

        [Fact]
        public async Task GetInvalidInvoices_SortsByCreatedAtDescending()
        {
            var older = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var newer = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            var newest = new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc);

            SetupRepos(
                invalidInvoices: [BuildInvalidInvoice(created: older)],
                extractionTotal: 1,
                securityLogs: [BuildUnhealthyLog(id: 1, created: newest), BuildUnhealthyLog(id: 2, created: newer)],
                securityTotal: 2);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var data = Prop<IEnumerable<object>>(result, "Data").ToList();

            Assert.Equal(newest, Prop<DateTime>(data[0], "CreatedAt"));
            Assert.Equal(newer, Prop<DateTime>(data[1], "CreatedAt"));
            Assert.Equal(older, Prop<DateTime>(data[2], "CreatedAt"));
        }

        [Fact]
        public async Task GetInvalidInvoices_OnlyExtractionFailures_ReturnsCorrectData()
        {
            SetupRepos(invalidInvoices: [BuildInvalidInvoice(), BuildInvalidInvoice()], extractionTotal: 2);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var data = Prop<IEnumerable<object>>(result, "Data").ToList();

            Assert.Equal(2, data.Count);
            Assert.All(data, d => Assert.Equal("ExtractionFailure", Prop<string>(d, "Type")));
        }

        [Fact]
        public async Task GetInvalidInvoices_OnlySecurityViolations_ReturnsCorrectData()
        {
            SetupRepos(securityLogs: [BuildUnhealthyLog(1), BuildUnhealthyLog(2)], securityTotal: 2);

            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            var data = Prop<IEnumerable<object>>(result, "Data").ToList();

            Assert.Equal(2, data.Count);
            Assert.All(data, d => Assert.Equal("SecurityViolation", Prop<string>(d, "Type")));
        }

        [Fact]
        public async Task GetInvalidInvoices_BothEmpty_ReturnsEmptyData()
        {
            SetupRepos();
            var result = await _sut.GetInvalidInvoicesAsync(1, 20, null);
            Assert.Empty(Prop<IEnumerable<object>>(result, "Data"));
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region Logging
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetInvalidInvoices_NullVendorId_LogsALL()
        {
            SetupRepos();
            await _sut.GetInvalidInvoicesAsync(1, 20, null);

            _mockLogger.Verify(
                l => l.Log(LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("ALL")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetInvalidInvoices_WithVendorId_LogsVendorIdString()
        {
            var vendorId = Guid.NewGuid();
            SetupRepos();
            await _sut.GetInvalidInvoicesAsync(1, 20, vendorId);

            _mockLogger.Verify(
                l => l.Log(LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(vendorId.ToString())),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion
    }
}
