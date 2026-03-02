using invoice_v1.src.Application.BackgroundServices;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Data;
using invoice_v1.src.Infrastructure.Repositories;
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
    // ─── SHIM: Fixes PostgreSQL jsonb mapping for SQLite ──────────────────
    public class TestDbContext : ApplicationDbContext
    {
        public TestDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    // SQLite doesn't support JsonDocument/jsonb directly
                    if (property.ClrType == typeof(JsonDocument))
                    {
                        property.SetColumnType(null); // Remove 'jsonb'
                        property.SetValueConverter(new ValueConverter<JsonDocument, string>(
                            v => v.RootElement.GetRawText(),
                            v => JsonDocument.Parse(v, default)
                        ));
                    }
                }
            }
        }
    }

    public class JobCreationServiceTests : IDisposable
    {
        private readonly Mock<IFileChangeLogRepository> _mockFileLogRepo;
        private readonly Mock<IJobService> _mockJobService;
        private readonly Mock<ILogger<JobCreationService>> _mockLogger;
        private readonly ApplicationDbContext _context;
        private readonly SqliteConnection _connection;
        private readonly JobCreationService _sut;
        private readonly IServiceProvider _serviceProvider;

        public JobCreationServiceTests()
        {
            _mockFileLogRepo = new Mock<IFileChangeLogRepository>();
            _mockJobService = new Mock<IJobService>();
            _mockLogger = new Mock<ILogger<JobCreationService>>();

            // 1. Setup SQLite In-Memory
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options;

            // Use the TestDbContext shim to avoid PostgreSQL jsonb errors
            _context = new TestDbContext(options);
            _context.Database.EnsureCreated();

            // 2. Setup Mock DI Container
            var services = new ServiceCollection();
            // Register as the base class so the service finds it
            services.AddSingleton<ApplicationDbContext>(_context);
            services.AddSingleton(_mockFileLogRepo.Object);
            services.AddSingleton(_mockJobService.Object);
            _serviceProvider = services.BuildServiceProvider();

            _sut = new JobCreationService(_serviceProvider, _mockLogger.Object);
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }

        private async Task InvokeDoWorkAsync(CancellationToken ct = default)
        {
            var method = typeof(JobCreationService).GetMethod("DoWorkAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            await (Task)method!.Invoke(_sut, [ct])!;
        }

        [Fact]
        public async Task DoWork_NoUnprocessedLogs_ReturnsEarly()
        {
            _mockFileLogRepo.Setup(r => r.GetUnprocessedHealthyLogsAsync(50))
                            .ReturnsAsync(new List<FileChangeLog>());

            await InvokeDoWorkAsync();

            _mockJobService.Verify(s => s.CreateJobFromLogAsync(It.IsAny<FileChangeLog>()), Times.Never);
        }

        [Fact]
        public async Task DoWork_WithHealthyLogs_CreatesJobsAndMarksProcessed()
        {
            var log = new FileChangeLog { FileId = "file-1", FileName = "test.pdf", Processed = false };
            _mockFileLogRepo.Setup(r => r.GetUnprocessedHealthyLogsAsync(50))
                            .ReturnsAsync(new List<FileChangeLog> { log });

            await InvokeDoWorkAsync();

            _mockJobService.Verify(s => s.CreateJobFromLogAsync(log), Times.Once);
            Assert.True(log.Processed);
            Assert.NotNull(log.ProcessedAt);
            _mockFileLogRepo.Verify(r => r.UpdateAsync(log), Times.Once);
        }

        [Fact]
        public async Task DoWork_JobCreationFails_RollsBackLogStatus()
        {
            var log = new FileChangeLog { FileId = "fail-file", Processed = false };
            _mockFileLogRepo.Setup(r => r.GetUnprocessedHealthyLogsAsync(50))
                            .ReturnsAsync(new List<FileChangeLog> { log });

            _mockJobService.Setup(s => s.CreateJobFromLogAsync(log))
                           .ThrowsAsync(new Exception("DB Error"));

            await InvokeDoWorkAsync();

            Assert.False(log.Processed); // Rollback verified
            _mockLogger.Verify(
                l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Failed to create job")),
                    It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task DoWork_ProcessingOneFails_ContinuesToNext()
        {
            var log1 = new FileChangeLog { FileId = "fail", Processed = false };
            var log2 = new FileChangeLog { FileId = "success", Processed = false };

            _mockFileLogRepo.Setup(r => r.GetUnprocessedHealthyLogsAsync(50))
                            .ReturnsAsync(new List<FileChangeLog> { log1, log2 });

            _mockJobService.Setup(s => s.CreateJobFromLogAsync(log1)).ThrowsAsync(new Exception("Boom"));
            _mockJobService.Setup(s => s.CreateJobFromLogAsync(log2)).Returns(Task.CompletedTask);

            await InvokeDoWorkAsync();

            Assert.False(log1.Processed);
            Assert.True(log2.Processed);
            _mockJobService.Verify(s => s.CreateJobFromLogAsync(It.IsAny<FileChangeLog>()), Times.Exactly(2));
        }
    }
}
