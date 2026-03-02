using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Data;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;
using System.Text.Json;
using Xunit;
using GoogleFile = Google.Apis.Drive.v3.Data.File;

namespace invoice_v1.tests.Services
{
    // Shim to handle jsonb mapping for SQLite
    public class TestDbContextMonitor : ApplicationDbContext
    {
        public TestDbContextMonitor(DbContextOptions<ApplicationDbContext> options) : base(options) { }
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

    public class DriveMonitoringServiceTests : IDisposable
    {
        private readonly Mock<IGoogleDriveService> _mockDriveService;
        private readonly Mock<ILogger<DriveMonitoringService>> _mockLogger;
        private readonly Mock<IConfiguration> _mockConfig;
        private readonly ApplicationDbContext _context;
        private readonly SqliteConnection _connection;
        private readonly DriveMonitoringService _sut;
        private readonly IServiceProvider _serviceProvider;

        public DriveMonitoringServiceTests()
        {
            _mockDriveService = new Mock<IGoogleDriveService>();
            _mockLogger = new Mock<ILogger<DriveMonitoringService>>();
            _mockConfig = new Mock<IConfiguration>();

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
            _context = new TestDbContextMonitor(options);
            _context.Database.EnsureCreated();

            _mockConfig.Setup(c => c["GoogleDrive:SharedFolderId"]).Returns("root-folder-123");

            var services = new ServiceCollection();
            services.AddSingleton<ApplicationDbContext>(_context);
            services.AddSingleton(_mockDriveService.Object);
            services.AddSingleton(_mockConfig.Object);
            _serviceProvider = services.BuildServiceProvider();

            _sut = new DriveMonitoringService(_mockLogger.Object, _serviceProvider);
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }

        private async Task InvokeDoWork(CancellationToken ct = default)
        {
            var method = typeof(DriveMonitoringService).GetMethod("DoWork", BindingFlags.NonPublic | BindingFlags.Instance);
            await (Task)method!.Invoke(_sut, [ct])!;
        }

        private async Task InvokeHydrate(CancellationToken ct = default)
        {
            var method = typeof(DriveMonitoringService).GetMethod("HydrateDictionaryFromDatabase", BindingFlags.NonPublic | BindingFlags.Instance);
            await (Task)method!.Invoke(_sut, [ct])!;
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region Detection Tests
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task DoWork_NewFile_CreatesUploadLog()
        {
            var driveFiles = new List<GoogleFile>
            {
                new() { Id = "file-1", Name = "invoice.pdf", MimeType = "application/pdf", ModifiedTime = DateTime.UtcNow, Size = 100 }
            };
            _mockDriveService.Setup(s => s.ListAllFilesRecursivelyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(driveFiles);

            await InvokeDoWork();

            var logs = await _context.FileChangeLogs.ToListAsync();
            Assert.Single(logs);
            Assert.Equal("Upload", logs[0].ChangeType);
            Assert.Equal("file-1", logs[0].FileId);
        }

        [Fact]
        public async Task DoWork_ExistingFileInDB_PreventsDuplicateUploadLog()
        {
            // 1. Arrange: File already exists in DB
            _context.FileChangeLogs.Add(new FileChangeLog
            {
                FileId = "existing-1",
                ChangeType = "Upload",
                DetectedAt = DateTime.UtcNow.AddHours(-1)
            });
            await _context.SaveChangesAsync();

            var driveFiles = new List<GoogleFile>
            {
                new() { Id = "existing-1", Name = "invoice.pdf", MimeType = "application/pdf", ModifiedTime = DateTime.UtcNow }
            };
            _mockDriveService.Setup(s => s.ListAllFilesRecursivelyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(driveFiles);

            // 2. Act
            await InvokeDoWork();

            // 3. Assert: Still only 1 log (no new one created)
            var logs = await _context.FileChangeLogs.ToListAsync();
            Assert.Single(logs);
        }

        [Fact]
        public async Task DoWork_ModifiedFile_CreatesModifiedLog()
        {
            // 1. Arrange: Memory knows about the file, but Drive shows a newer time
            var lastSeen = DateTime.UtcNow.AddMinutes(-30);
            var driveTime = DateTime.UtcNow; // New time

            // Manually set internal memory state using reflection if needed, 
            // OR hydrate from DB which is cleaner
            _context.FileChangeLogs.Add(new FileChangeLog
            {
                FileId = "mod-1",
                ChangeType = "Upload",
                GoogleDriveModifiedTime = lastSeen,
                DetectedAt = DateTime.UtcNow.AddDays(-1)
            });
            await _context.SaveChangesAsync();
            await InvokeHydrate(); // Hydrate memory from DB

            var driveFiles = new List<GoogleFile>
            {
                new() { Id = "mod-1", Name = "invoice.pdf", MimeType = "application/pdf", ModifiedTime = driveTime }
            };
            _mockDriveService.Setup(s => s.ListAllFilesRecursivelyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(driveFiles);

            // 2. Act
            await InvokeDoWork();

            // 3. Assert
            var logs = await _context.FileChangeLogs.OrderByDescending(l => l.DetectedAt).ToListAsync();
            Assert.Equal(2, logs.Count);
            Assert.Equal("Modified", logs[0].ChangeType);
        }

        [Fact]
        public async Task DoWork_FileMissingFromDrive_CreatesDeletedLog()
        {
            // 1. Arrange: File exists in memory/DB but not in the new Drive list
            _context.FileChangeLogs.Add(new FileChangeLog { FileId = "del-1", ChangeType = "Upload" });
            await _context.SaveChangesAsync();
            await InvokeHydrate();

            _mockDriveService.Setup(s => s.ListAllFilesRecursivelyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(new List<GoogleFile>()); // Drive is empty

            // 2. Act
            await InvokeDoWork();

            // 3. Assert
            var deleteLog = await _context.FileChangeLogs.FirstOrDefaultAsync(l => l.ChangeType == "Deleted");
            Assert.NotNull(deleteLog);
            Assert.Equal("del-1", deleteLog.FileId);
        }

        [Fact]
        public async Task DoWork_RestoredFile_DetectsUploadAfterDeletion()
        {
            // 1. Arrange: Mark as deleted in memory
            _context.FileChangeLogs.Add(new FileChangeLog { FileId = "rest-1", ChangeType = "Deleted" });
            await _context.SaveChangesAsync();
            await InvokeHydrate();

            var driveFiles = new List<GoogleFile>
            {
                new() { Id = "rest-1", Name = "restored.pdf", MimeType = "application/pdf", ModifiedTime = DateTime.UtcNow }
            };
            _mockDriveService.Setup(s => s.ListAllFilesRecursivelyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(driveFiles);

            // 2. Act
            await InvokeDoWork();

            // 3. Assert
            var logs = await _context.FileChangeLogs.Where(l => l.FileId == "rest-1").ToListAsync();
            Assert.Contains(logs, l => l.ChangeType == "Upload");
        }

        #endregion
    }
}
