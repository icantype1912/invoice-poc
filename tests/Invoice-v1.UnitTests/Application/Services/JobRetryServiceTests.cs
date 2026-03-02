using invoice_v1.src.Application.BackgroundServices;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace invoice_v1.tests.Application.BackgroundServices
{
    // Reuse the shim from the previous turn to fix JsonDocument mapping
    public class TestDbContextRetry : ApplicationDbContext
    {
        public TestDbContextRetry(DbContextOptions<ApplicationDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
            {
                if (property.ClrType == typeof(JsonDocument))
                {
                    property.SetColumnType(null);
                    property.SetValueConverter(new ValueConverter<JsonDocument, string>(
                        v => v.RootElement.GetRawText(),
                        v => JsonDocument.Parse(v, default)));
                }
            }
        }
    }

    public class JobRetryServiceTests : IDisposable
    {
        private readonly Mock<IJobService> _mockJobService;
        private readonly Mock<ILogger<JobRetryService>> _mockLogger;
        private readonly ApplicationDbContext _context;
        private readonly SqliteConnection _connection;
        private readonly JobRetryService _sut;
        private readonly IServiceProvider _serviceProvider;

        public JobRetryServiceTests()
        {
            _mockJobService = new Mock<IJobService>();
            _mockLogger = new Mock<ILogger<JobRetryService>>();

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new TestDbContextRetry(options);
            _context.Database.EnsureCreated();

            var services = new ServiceCollection();
            services.AddSingleton<ApplicationDbContext>(_context);
            services.AddSingleton(_mockJobService.Object);
            _serviceProvider = services.BuildServiceProvider();

            _sut = new JobRetryService(_serviceProvider, _mockLogger.Object);
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }

        private async Task InvokeProcessRetriesAsync(CancellationToken ct = default)
        {
            var method = typeof(JobRetryService).GetMethod("ProcessRetriesAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            await (Task)method!.Invoke(_sut, [ct])!;
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region ProcessRetriesAsync Tests
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ProcessRetries_NoJobs_DoesNothing()
        {
            await InvokeProcessRetriesAsync();

            _mockJobService.Verify(s => s.ProcessPendingJobAsync(It.IsAny<JobQueue>()), Times.Never);
            _mockLogger.Verify(
                l => l.Log(LogLevel.Information, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Found")),
                    It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }

        [Fact]
        public async Task ProcessRetries_JobDueForRetry_CallsProcessPendingJob()
        {
            // Setup a job that is PENDING and whose NextRetryAt is in the past
            var job = new JobQueue
            {
                Id = Guid.NewGuid(),
                JobType = "TEST",
                Status = nameof(JobStatus.PENDING),
                NextRetryAt = DateTime.UtcNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.JobQueues.Add(job);
            await _context.SaveChangesAsync();

            await InvokeProcessRetriesAsync();

            _mockJobService.Verify(s => s.ProcessPendingJobAsync(It.Is<JobQueue>(j => j.Id == job.Id)), Times.Once);
            _mockLogger.Verify(
                l => l.Log(LogLevel.Information, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Found 1 jobs")),
                    It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessRetries_JobNotDueYet_SkipsJob()
        {
            // NextRetryAt is in the future
            var job = new JobQueue
            {
                Id = Guid.NewGuid(),
                Status = nameof(JobStatus.PENDING),
                NextRetryAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.JobQueues.Add(job);
            await _context.SaveChangesAsync();

            await InvokeProcessRetriesAsync();

            _mockJobService.Verify(s => s.ProcessPendingJobAsync(It.IsAny<JobQueue>()), Times.Never);
        }

        [Fact]
        public async Task ProcessRetries_JobWithWrongStatus_SkipsJob()
        {
            // Status is NOT Pending, even though NextRetryAt is in the past
            var job = new JobQueue
            {
                Id = Guid.NewGuid(),
                Status = nameof(JobStatus.FAILED),
                NextRetryAt = DateTime.UtcNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.JobQueues.Add(job);
            await _context.SaveChangesAsync();

            await InvokeProcessRetriesAsync();

            _mockJobService.Verify(s => s.ProcessPendingJobAsync(It.IsAny<JobQueue>()), Times.Never);
        }

        [Fact]
        public async Task ProcessRetries_MixedJobs_ProcessesOnlyDueOnes()
        {
            var dueJob = new JobQueue
            {
                Id = Guid.NewGuid(),
                Status = nameof(JobStatus.PENDING),
                NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var futureJob = new JobQueue
            {
                Id = Guid.NewGuid(),
                Status = nameof(JobStatus.PENDING),
                NextRetryAt = DateTime.UtcNow.AddMinutes(10),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.JobQueues.AddRange(dueJob, futureJob);
            await _context.SaveChangesAsync();

            await InvokeProcessRetriesAsync();

            _mockJobService.Verify(s => s.ProcessPendingJobAsync(It.Is<JobQueue>(j => j.Id == dueJob.Id)), Times.Once);
            _mockJobService.Verify(s => s.ProcessPendingJobAsync(It.Is<JobQueue>(j => j.Id == futureJob.Id)), Times.Never);
        }

        [Fact]
        public async Task ProcessRetries_NullNextRetryAt_SkipsJob()
        {
            var job = new JobQueue
            {
                Id = Guid.NewGuid(),
                Status = nameof(JobStatus.PENDING),
                NextRetryAt = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.JobQueues.Add(job);
            await _context.SaveChangesAsync();

            await InvokeProcessRetriesAsync();

            _mockJobService.Verify(s => s.ProcessPendingJobAsync(It.IsAny<JobQueue>()), Times.Never);
        }

        #endregion
    }
}
