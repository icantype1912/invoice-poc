using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Services;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace invoice_v1.tests.Application.Services
{
    public class JobServiceTests
    {
        private readonly Mock<IJobRepository> _mockJobRepo;
        private readonly Mock<IInvalidInvoiceRepository> _mockInvalidRepo;
        private readonly Mock<ILogger<JobService>> _mockLogger;
        private readonly JobService _sut;

        public JobServiceTests()
        {
            _mockJobRepo = new Mock<IJobRepository>();
            _mockInvalidRepo = new Mock<IInvalidInvoiceRepository>();
            _mockLogger = new Mock<ILogger<JobService>>();

            _sut = new JobService(
                _mockJobRepo.Object,
                _mockInvalidRepo.Object,
                _mockLogger.Object);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────

        private static JobQueue BuildJob(
            string status = "PENDING",
            string? fileId = "file-123",
            string? uploader = null,
            int retryCount = 0) => new()
            {
                Id = Guid.NewGuid(),
                JobType = nameof(JobType.INVOICE_EXTRACTION),
                Status = status,
                PayloadJson = JsonSerializer.SerializeToDocument(new
                {
                    fileId,
                    originalName = "invoice.pdf",
                    uploader
                }),
                RetryCount = retryCount,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

        private static FileChangeLog BuildLog(
            string? fileId = "file-123",
            string? fileName = "invoice.pdf") => new()
            {
                FileId = fileId,
                FileName = fileName,
                MimeType = "application/pdf",
                FileSize = 1024,
                UploadedByVendorId = null,
                DetectedAt = DateTime.UtcNow
            };

        private static JsonDocument BuildErrorDoc(string msg = "error") =>
            JsonSerializer.SerializeToDocument(new { error = msg });

        private void SetupSaveAndUpdate()
        {
            _mockJobRepo.Setup(r => r.UpdateAsync(It.IsAny<JobQueue>())).Returns(Task.CompletedTask);
            _mockJobRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region GetJobByIdAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetJobByIdAsync_NotFound_ReturnsNull()
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((JobQueue?)null);
            Assert.Null(await _sut.GetJobByIdAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task GetJobByIdAsync_Found_ReturnsMappedDto()
        {
            var job = BuildJob();
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);

            var dto = await _sut.GetJobByIdAsync(job.Id);

            Assert.NotNull(dto);
            Assert.Equal(job.Id, dto!.Id);
            Assert.Equal(job.Status, dto.Status);
            Assert.Equal(job.JobType, dto.JobType);
        }

        [Fact]
        public async Task GetJobByIdAsync_Found_MapsAllFields()
        {
            var job = BuildJob(retryCount: 2);
            job.LockedBy = "worker-1";
            job.LockedAt = DateTime.UtcNow;
            job.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);

            var dto = await _sut.GetJobByIdAsync(job.Id);

            Assert.Equal(2, dto!.RetryCount);
            Assert.Equal("worker-1", dto.LockedBy);
            Assert.NotNull(dto.LockedAt);
            Assert.NotNull(dto.NextRetryAt);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetJobEntityByIdAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetJobEntityByIdAsync_NotFound_ReturnsNull()
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((JobQueue?)null);
            Assert.Null(await _sut.GetJobEntityByIdAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task GetJobEntityByIdAsync_Found_ReturnsEntity()
        {
            var job = BuildJob();
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);

            var result = await _sut.GetJobEntityByIdAsync(job.Id);

            Assert.NotNull(result);
            Assert.Same(job, result);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetJobsAsync — Pagination
        // ────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task GetJobsAsync_PageLessThan1_NormalizesTo1(int badPage)
        {
            _mockJobRepo.Setup(r => r.GetJobsAsync(null, 1, 10, null))
                        .ReturnsAsync((new List<JobQueue>(), 0));

            await _sut.GetJobsAsync(null, badPage, 10, null);

            _mockJobRepo.Verify(r => r.GetJobsAsync(null, 1, 10, null), Times.Once);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public async Task GetJobsAsync_PageSizeLessThan1_NormalizesTo10(int badSize)
        {
            _mockJobRepo.Setup(r => r.GetJobsAsync(null, 1, 10, null))
                        .ReturnsAsync((new List<JobQueue>(), 0));

            await _sut.GetJobsAsync(null, 1, badSize, null);

            _mockJobRepo.Verify(r => r.GetJobsAsync(null, 1, 10, null), Times.Once);
        }

        [Theory]
        [InlineData(101)]
        [InlineData(500)]
        public async Task GetJobsAsync_PageSizeOver100_NormalizesTo100(int bigSize)
        {
            _mockJobRepo.Setup(r => r.GetJobsAsync(null, 1, 100, null))
                        .ReturnsAsync((new List<JobQueue>(), 0));

            await _sut.GetJobsAsync(null, 1, bigSize, null);

            _mockJobRepo.Verify(r => r.GetJobsAsync(null, 1, 100, null), Times.Once);
        }

        [Fact]
        public async Task GetJobsAsync_ValidParams_PassedThrough()
        {
            var vendorId = Guid.NewGuid();
            _mockJobRepo.Setup(r => r.GetJobsAsync(JobStatus.PENDING, 2, 25, vendorId))
                        .ReturnsAsync((new List<JobQueue>(), 0));

            await _sut.GetJobsAsync(JobStatus.PENDING, 2, 25, vendorId);

            _mockJobRepo.Verify(r => r.GetJobsAsync(JobStatus.PENDING, 2, 25, vendorId), Times.Once);
        }

        [Fact]
        public async Task GetJobsAsync_ReturnsMappedDtosAndTotal()
        {
            var job = BuildJob();
            _mockJobRepo.Setup(r => r.GetJobsAsync(null, 1, 10, null))
                        .ReturnsAsync((new List<JobQueue> { job }, 42));

            var (dtos, total) = await _sut.GetJobsAsync(null, 1, 10, null);

            Assert.Equal(42, total);
            Assert.Single(dtos);
            Assert.Equal(job.Id, dtos[0].Id);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CanVendorAccessJobAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CanVendorAccessJobAsync_JobNotFound_ReturnsFalse()
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((JobQueue?)null);
            Assert.False(await _sut.CanVendorAccessJobAsync(Guid.NewGuid(), Guid.NewGuid()));
        }

        [Fact]
        public async Task CanVendorAccessJobAsync_NullPayload_ReturnsFalse()
        {
            var job = new JobQueue
            {
                Id = Guid.NewGuid(),
                JobType = "INVOICE_EXTRACTION",
                Status = "PENDING",
                PayloadJson = null!,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);

            Assert.False(await _sut.CanVendorAccessJobAsync(job.Id, Guid.NewGuid()));
        }

        [Fact]
        public async Task CanVendorAccessJobAsync_UploaderMissing_ReturnsFalse()
        {
            var job = BuildJob(uploader: null);
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);

            Assert.False(await _sut.CanVendorAccessJobAsync(job.Id, Guid.NewGuid()));
        }

        [Fact]
        public async Task CanVendorAccessJobAsync_UploaderMatches_ReturnsTrue()
        {
            var vendorId = Guid.NewGuid();
            var job = BuildJob(uploader: vendorId.ToString());
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);

            Assert.True(await _sut.CanVendorAccessJobAsync(job.Id, vendorId));
        }

        [Fact]
        public async Task CanVendorAccessJobAsync_UploaderMismatch_ReturnsFalse()
        {
            var job = BuildJob(uploader: Guid.NewGuid().ToString());
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);

            Assert.False(await _sut.CanVendorAccessJobAsync(job.Id, Guid.NewGuid()));
        }

        [Fact]
        public async Task CanVendorAccessJobAsync_InvalidGuidInUploader_ReturnsFalse()
        {
            var job = BuildJob(uploader: "not-a-guid");
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);

            Assert.False(await _sut.CanVendorAccessJobAsync(job.Id, Guid.NewGuid()));
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateJobFromLogAsync — Guard Clauses
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateJobFromLogAsync_NullLog_ThrowsArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _sut.CreateJobFromLogAsync(null!));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task CreateJobFromLogAsync_BlankFileId_ThrowsArgumentException(string? fileId)
        {
            var log = BuildLog(fileId: fileId);
            await Assert.ThrowsAsync<ArgumentException>(
                () => _sut.CreateJobFromLogAsync(log));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task CreateJobFromLogAsync_BlankFileName_ThrowsArgumentException(string? fileName)
        {
            var log = BuildLog(fileName: fileName);
            await Assert.ThrowsAsync<ArgumentException>(
                () => _sut.CreateJobFromLogAsync(log));
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateJobFromLogAsync — Idempotency
        // ────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("PENDING")]
        [InlineData("PROCESSING")]
        [InlineData("COMPLETED")]
        public async Task CreateJobFromLogAsync_ActiveJobExists_SkipsCreation(string activeStatus)
        {
            var existing = BuildJob(status: activeStatus);
            _mockJobRepo.Setup(r => r.GetJobsByFileIdAsync("file-123"))
                        .ReturnsAsync(new List<JobQueue> { existing });

            await _sut.CreateJobFromLogAsync(BuildLog());

            _mockJobRepo.Verify(r => r.CreateAsync(It.IsAny<JobQueue>()), Times.Never);
        }

        [Theory]
        [InlineData("FAILED")]
        [InlineData("INVALID")]
        public async Task CreateJobFromLogAsync_OnlyTerminalJobsExist_CreatesNewJob(string terminalStatus)
        {
            var existing = BuildJob(status: terminalStatus);
            _mockJobRepo.Setup(r => r.GetJobsByFileIdAsync("file-123"))
                        .ReturnsAsync(new List<JobQueue> { existing });
            _mockJobRepo.Setup(r => r.CreateAsync(It.IsAny<JobQueue>())).Returns(Task.CompletedTask);
            _mockJobRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.CreateJobFromLogAsync(BuildLog());

            _mockJobRepo.Verify(r => r.CreateAsync(It.IsAny<JobQueue>()), Times.Once);
        }

        [Fact]
        public async Task CreateJobFromLogAsync_NoExistingJobs_CreatesJob()
        {
            _mockJobRepo.Setup(r => r.GetJobsByFileIdAsync("file-123"))
                        .ReturnsAsync(new List<JobQueue>());
            _mockJobRepo.Setup(r => r.CreateAsync(It.IsAny<JobQueue>())).Returns(Task.CompletedTask);
            _mockJobRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.CreateJobFromLogAsync(BuildLog());

            _mockJobRepo.Verify(r => r.CreateAsync(It.IsAny<JobQueue>()), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateJobFromLogAsync — Payload & Job Properties
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateJobFromLogAsync_CreatedJob_HasPendingStatus()
        {
            _mockJobRepo.Setup(r => r.GetJobsByFileIdAsync(It.IsAny<string>()))
                        .ReturnsAsync(new List<JobQueue>());
            JobQueue? created = null;
            _mockJobRepo.Setup(r => r.CreateAsync(It.IsAny<JobQueue>()))
                        .Callback<JobQueue>(j => created = j)
                        .Returns(Task.CompletedTask);
            _mockJobRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.CreateJobFromLogAsync(BuildLog());

            Assert.Equal(nameof(JobStatus.PENDING), created!.Status);
        }

        [Fact]
        public async Task CreateJobFromLogAsync_CreatedJob_HasCorrectJobType()
        {
            _mockJobRepo.Setup(r => r.GetJobsByFileIdAsync(It.IsAny<string>()))
                        .ReturnsAsync(new List<JobQueue>());
            JobQueue? created = null;
            _mockJobRepo.Setup(r => r.CreateAsync(It.IsAny<JobQueue>()))
                        .Callback<JobQueue>(j => created = j)
                        .Returns(Task.CompletedTask);
            _mockJobRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.CreateJobFromLogAsync(BuildLog());

            Assert.Equal(nameof(JobType.INVOICE_EXTRACTION), created!.JobType);
        }

        [Fact]
        public async Task CreateJobFromLogAsync_CreatedJob_RetryCountIsZero()
        {
            _mockJobRepo.Setup(r => r.GetJobsByFileIdAsync(It.IsAny<string>()))
                        .ReturnsAsync(new List<JobQueue>());
            JobQueue? created = null;
            _mockJobRepo.Setup(r => r.CreateAsync(It.IsAny<JobQueue>()))
                        .Callback<JobQueue>(j => created = j)
                        .Returns(Task.CompletedTask);
            _mockJobRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.CreateJobFromLogAsync(BuildLog());

            Assert.Equal(0, created!.RetryCount);
        }

        [Fact]
        public async Task CreateJobFromLogAsync_PayloadContainsFileIdAndFileName()
        {
            _mockJobRepo.Setup(r => r.GetJobsByFileIdAsync(It.IsAny<string>()))
                        .ReturnsAsync(new List<JobQueue>());
            JobQueue? created = null;
            _mockJobRepo.Setup(r => r.CreateAsync(It.IsAny<JobQueue>()))
                        .Callback<JobQueue>(j => created = j)
                        .Returns(Task.CompletedTask);
            _mockJobRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.CreateJobFromLogAsync(BuildLog(fileId: "drive-xyz", fileName: "po.pdf"));

            var payload = created!.PayloadJson!.RootElement;
            Assert.Equal("drive-xyz", payload.GetProperty("fileId").GetString());
            Assert.Equal("po.pdf", payload.GetProperty("originalName").GetString());
        }

        [Fact]
        public async Task CreateJobFromLogAsync_PayloadContainsUploaderVendorId()
        {
            var vendorId = Guid.NewGuid();
            var log = BuildLog();
            log.UploadedByVendorId = vendorId;

            _mockJobRepo.Setup(r => r.GetJobsByFileIdAsync(It.IsAny<string>()))
                        .ReturnsAsync(new List<JobQueue>());
            JobQueue? created = null;
            _mockJobRepo.Setup(r => r.CreateAsync(It.IsAny<JobQueue>()))
                        .Callback<JobQueue>(j => created = j)
                        .Returns(Task.CompletedTask);
            _mockJobRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.CreateJobFromLogAsync(log);

            var payload = created!.PayloadJson!.RootElement;
            Assert.Equal(vendorId.ToString(), payload.GetProperty("uploader").GetString());
        }

        [Fact]
        public async Task CreateJobFromLogAsync_CallsSaveChangesAsync()
        {
            _mockJobRepo.Setup(r => r.GetJobsByFileIdAsync(It.IsAny<string>()))
                        .ReturnsAsync(new List<JobQueue>());
            _mockJobRepo.Setup(r => r.CreateAsync(It.IsAny<JobQueue>())).Returns(Task.CompletedTask);
            _mockJobRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.CreateJobFromLogAsync(BuildLog());

            _mockJobRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CompleteJobAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CompleteJobAsync_JobNotFound_Throws()
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((JobQueue?)null);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CompleteJobAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task CompleteJobAsync_SetsStatusToCompleted()
        {
            var job = BuildJob(status: "PENDING");
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            SetupSaveAndUpdate();

            await _sut.CompleteJobAsync(job.Id);

            Assert.Equal(nameof(JobStatus.COMPLETED), job.Status);
        }

        [Fact]
        public async Task CompleteJobAsync_ClearsLockedByAndLockedAt()
        {
            var job = BuildJob();
            job.LockedBy = "worker-1";
            job.LockedAt = DateTime.UtcNow;
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            SetupSaveAndUpdate();

            await _sut.CompleteJobAsync(job.Id);

            Assert.Null(job.LockedBy);
            Assert.Null(job.LockedAt);
        }

        [Fact]
        public async Task CompleteJobAsync_CallsUpdateAndSave()
        {
            var job = BuildJob();
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            SetupSaveAndUpdate();

            await _sut.CompleteJobAsync(job.Id);

            _mockJobRepo.Verify(r => r.UpdateAsync(job), Times.Once);
            _mockJobRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region MarkFailedAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task MarkFailedAsync_JobNotFound_Throws()
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((JobQueue?)null);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.MarkFailedAsync(Guid.NewGuid(), BuildErrorDoc()));
        }

        [Fact]
        public async Task MarkFailedAsync_SetsStatusToInvalid()
        {
            var job = BuildJob();
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            SetupSaveAndUpdate();
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job); // for CreateInvalidInvoice call
            _mockInvalidRepo.Setup(r => r.CreateAsync(It.IsAny<InvalidInvoice>())).Returns(Task.CompletedTask);
            _mockInvalidRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.MarkFailedAsync(job.Id, BuildErrorDoc());

            Assert.Equal(nameof(JobStatus.INVALID), job.Status);
        }

        [Fact]
        public async Task MarkFailedAsync_ClearsRetryAndLockFields()
        {
            var job = BuildJob();
            job.LockedBy = "worker-1";
            job.LockedAt = DateTime.UtcNow;
            job.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            SetupSaveAndUpdate();
            _mockInvalidRepo.Setup(r => r.CreateAsync(It.IsAny<InvalidInvoice>())).Returns(Task.CompletedTask);
            _mockInvalidRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.MarkFailedAsync(job.Id, BuildErrorDoc());

            Assert.Null(job.LockedBy);
            Assert.Null(job.LockedAt);
            Assert.Null(job.NextRetryAt);
        }

        [Fact]
        public async Task MarkFailedAsync_SetsErrorMessage()
        {
            var job = BuildJob();
            var errDoc = BuildErrorDoc("extraction failed");
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            SetupSaveAndUpdate();
            _mockInvalidRepo.Setup(r => r.CreateAsync(It.IsAny<InvalidInvoice>())).Returns(Task.CompletedTask);
            _mockInvalidRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.MarkFailedAsync(job.Id, errDoc);

            Assert.Same(errDoc, job.ErrorMessage);
        }

        [Fact]
        public async Task MarkFailedAsync_CreatesInvalidInvoiceEntry()
        {
            var job = BuildJob();
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            SetupSaveAndUpdate();
            _mockInvalidRepo.Setup(r => r.CreateAsync(It.IsAny<InvalidInvoice>())).Returns(Task.CompletedTask);
            _mockInvalidRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.MarkFailedAsync(job.Id, BuildErrorDoc());

            _mockInvalidRepo.Verify(r => r.CreateAsync(It.IsAny<InvalidInvoice>()), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region MarkInvalidAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task MarkInvalidAsync_JobNotFound_Throws()
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((JobQueue?)null);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.MarkInvalidAsync(Guid.NewGuid(), BuildErrorDoc()));
        }

        [Fact]
        public async Task MarkInvalidAsync_SetsStatusToInvalid()
        {
            var job = BuildJob(status: "PENDING");
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            SetupSaveAndUpdate();

            await _sut.MarkInvalidAsync(job.Id, BuildErrorDoc());

            Assert.Equal(nameof(JobStatus.INVALID), job.Status);
        }

        [Fact]
        public async Task MarkInvalidAsync_ClearsRetryAndLockFields()
        {
            var job = BuildJob();
            job.LockedBy = "worker-1";
            job.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            SetupSaveAndUpdate();

            await _sut.MarkInvalidAsync(job.Id, BuildErrorDoc());

            Assert.Null(job.LockedBy);
            Assert.Null(job.NextRetryAt);
        }

        [Fact]
        public async Task MarkInvalidAsync_DoesNotCreateInvalidInvoiceEntry()
        {
            var job = BuildJob();
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            SetupSaveAndUpdate();

            await _sut.MarkInvalidAsync(job.Id, BuildErrorDoc());

            _mockInvalidRepo.Verify(r => r.CreateAsync(It.IsAny<InvalidInvoice>()), Times.Never);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateInvalidInvoiceFromJobAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateInvalidInvoiceFromJobAsync_JobNotFound_ReturnsWithoutCreating()
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((JobQueue?)null);

            await _sut.CreateInvalidInvoiceFromJobAsync(Guid.NewGuid(), BuildErrorDoc());

            _mockInvalidRepo.Verify(r => r.CreateAsync(It.IsAny<InvalidInvoice>()), Times.Never);
        }

        [Fact]
        public async Task CreateInvalidInvoiceFromJobAsync_NullPayload_LogsWarningAndReturns()
        {
            var job = new JobQueue
            {
                Id = Guid.NewGuid(),
                JobType = "INVOICE_EXTRACTION",
                Status = "INVALID",
                PayloadJson = null!,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);

            await _sut.CreateInvalidInvoiceFromJobAsync(job.Id, BuildErrorDoc());

            _mockInvalidRepo.Verify(r => r.CreateAsync(It.IsAny<InvalidInvoice>()), Times.Never);
        }

        [Fact]
        public async Task CreateInvalidInvoiceFromJobAsync_MissingFileId_LogsWarningAndReturns()
        {
            var job = new JobQueue
            {
                Id = Guid.NewGuid(),
                JobType = "INVOICE_EXTRACTION",
                Status = "INVALID",
                PayloadJson = JsonSerializer.SerializeToDocument(new { originalName = "test.pdf" }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);

            await _sut.CreateInvalidInvoiceFromJobAsync(job.Id, BuildErrorDoc());

            _mockInvalidRepo.Verify(r => r.CreateAsync(It.IsAny<InvalidInvoice>()), Times.Never);
        }

        [Fact]
        public async Task CreateInvalidInvoiceFromJobAsync_ValidPayload_CreatesInvalidInvoice()
        {
            var job = BuildJob();
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            _mockInvalidRepo.Setup(r => r.CreateAsync(It.IsAny<InvalidInvoice>())).Returns(Task.CompletedTask);
            _mockInvalidRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.CreateInvalidInvoiceFromJobAsync(job.Id, BuildErrorDoc());

            _mockInvalidRepo.Verify(r => r.CreateAsync(It.IsAny<InvalidInvoice>()), Times.Once);
        }

        [Fact]
        public async Task CreateInvalidInvoiceFromJobAsync_MapsJobIdAndFileId()
        {
            var job = BuildJob(fileId: "drive-abc");
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            InvalidInvoice? created = null;
            _mockInvalidRepo.Setup(r => r.CreateAsync(It.IsAny<InvalidInvoice>()))
                            .Callback<InvalidInvoice>(inv => created = inv)
                            .Returns(Task.CompletedTask);
            _mockInvalidRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.CreateInvalidInvoiceFromJobAsync(job.Id, BuildErrorDoc());

            Assert.Equal(job.Id, created!.JobId);
            Assert.Equal("drive-abc", created.FileId);
        }

        [Fact]
        public async Task CreateInvalidInvoiceFromJobAsync_MapsVendorIdFromUploader()
        {
            var vendorId = Guid.NewGuid();
            var job = BuildJob(uploader: vendorId.ToString());
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            InvalidInvoice? created = null;
            _mockInvalidRepo.Setup(r => r.CreateAsync(It.IsAny<InvalidInvoice>()))
                            .Callback<InvalidInvoice>(inv => created = inv)
                            .Returns(Task.CompletedTask);
            _mockInvalidRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _sut.CreateInvalidInvoiceFromJobAsync(job.Id, BuildErrorDoc());

            Assert.Equal(vendorId, created!.VendorId);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region RequeueJobAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task RequeueJobAsync_JobNotFound_Throws()
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((JobQueue?)null);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.RequeueJobAsync(Guid.NewGuid()));
        }

        [Theory]
        [InlineData("PENDING")]
        [InlineData("PROCESSING")]
        [InlineData("COMPLETED")]
        public async Task RequeueJobAsync_NonTerminalStatus_Throws(string status)
        {
            var job = BuildJob(status: status);
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.RequeueJobAsync(job.Id));
        }

        [Theory]
        [InlineData("FAILED")]
        [InlineData("INVALID")]
        public async Task RequeueJobAsync_TerminalStatus_ResetsJobToPending(string terminalStatus)
        {
            var job = BuildJob(status: terminalStatus);
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            _mockInvalidRepo.Setup(r => r.DeleteByJobIdAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
            SetupSaveAndUpdate();

            await _sut.RequeueJobAsync(job.Id);

            Assert.Equal(nameof(JobStatus.PENDING), job.Status);
        }

        [Fact]
        public async Task RequeueJobAsync_ResetsRetryCountAndClearsErrorAndLocks()
        {
            var job = BuildJob(status: "INVALID", retryCount: 3);
            job.ErrorMessage = BuildErrorDoc();
            job.LockedBy = "worker-1";
            job.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            _mockInvalidRepo.Setup(r => r.DeleteByJobIdAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
            SetupSaveAndUpdate();

            await _sut.RequeueJobAsync(job.Id);

            Assert.Equal(0, job.RetryCount);
            Assert.Null(job.ErrorMessage);
            Assert.Null(job.LockedBy);
            Assert.Null(job.NextRetryAt);
        }

        [Fact]
        public async Task RequeueJobAsync_DeletesFromInvalidInvoicesTable()
        {
            var job = BuildJob(status: "INVALID");
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            _mockInvalidRepo.Setup(r => r.DeleteByJobIdAsync(job.Id)).Returns(Task.CompletedTask);
            SetupSaveAndUpdate();

            await _sut.RequeueJobAsync(job.Id);

            _mockInvalidRepo.Verify(r => r.DeleteByJobIdAsync(job.Id), Times.Once);
        }

        [Fact]
        public async Task RequeueJobAsync_CallsProcessPendingJob()
        {
            var job = BuildJob(status: "FAILED");
            _mockJobRepo.Setup(r => r.GetByIdAsync(job.Id)).ReturnsAsync(job);
            _mockInvalidRepo.Setup(r => r.DeleteByJobIdAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
            SetupSaveAndUpdate();

            await _sut.RequeueJobAsync(job.Id);

            // ProcessPendingJobAsync calls UpdateAsync + SaveChangesAsync a second time
            _mockJobRepo.Verify(r => r.UpdateAsync(It.IsAny<JobQueue>()), Times.Exactly(2));
            _mockJobRepo.Verify(r => r.SaveChangesAsync(), Times.Exactly(2));
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region ProcessPendingJobAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ProcessPendingJobAsync_ClearsNextRetryAt()
        {
            var job = BuildJob();
            job.NextRetryAt = DateTime.UtcNow.AddMinutes(10);
            SetupSaveAndUpdate();

            await _sut.ProcessPendingJobAsync(job);

            Assert.Null(job.NextRetryAt);
        }

        [Fact]
        public async Task ProcessPendingJobAsync_CallsUpdateAndSave()
        {
            var job = BuildJob();
            SetupSaveAndUpdate();

            await _sut.ProcessPendingJobAsync(job);

            _mockJobRepo.Verify(r => r.UpdateAsync(job), Times.Once);
            _mockJobRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        #endregion
    }
}
