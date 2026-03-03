using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Repositories;
using invoice_v1.tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace invoice_v1.tests.Infrastructure.Repositories
{
    public class AnalyticsRepositoryTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly TestDbContext _context;
        private readonly AnalyticsRepository _sut;

        public AnalyticsRepositoryTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = new DbContextOptionsBuilder<src.Infrastructure.Data.ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new TestDbContext(options);
            _context.Database.EnsureCreated();

            _sut = new AnalyticsRepository(_context);
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }

        [Fact]
        public async Task GetRevenueTrendDataAsync_FiltersByDate()
        {
            // Arrange
            var vendorId = Guid.NewGuid();
            _context.Users.Add(new User { Id = vendorId, Email = "v1@ex.com", PasswordHash = "x", PasswordSalt = "x" });
            var date = DateTime.UtcNow.Date;
            _context.Invoices.AddRange(
                new Invoice { Id = Guid.NewGuid(), TotalAmount = 100, CreatedAt = date, UploadedByVendorId = vendorId, DriveFileId = "file-trend-1" }
            );
            await _context.SaveChangesAsync();

            // Act
            var result = await _sut.GetRevenueTrendDataAsync(date.AddDays(-1), date.AddDays(1), null);

            // Assert
            Assert.Single(result);
            Assert.Equal(100, result[0].Amount);
        }

        [Fact]
        public async Task GetRevenueTrendDataAsync_RespectsDateRange()
        {
            // Arrange
            var date1 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var date2 = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
            var date3 = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);

            _context.Invoices.AddRange(
                new Invoice { Id = Guid.NewGuid(), TotalAmount = 100, CreatedAt = date1, DriveFileId = "file-range-1" },
                new Invoice { Id = Guid.NewGuid(), TotalAmount = 200, CreatedAt = date2, DriveFileId = "file-range-2" },
                new Invoice { Id = Guid.NewGuid(), TotalAmount = 300, CreatedAt = date3, DriveFileId = "file-range-3" }
            );
            await _context.SaveChangesAsync();

            // Act
            var result = await _sut.GetRevenueTrendDataAsync(new DateTime(2025, 1, 15), new DateTime(2025, 2, 15), null);

            // Assert
            Assert.Single(result);
            Assert.Equal(200, result[0].Amount);
        }

        [Fact]
        public async Task GetRevenueTrendDataAsync_RespectsVendorId()
        {
            // Arrange
            var vendor1 = Guid.NewGuid();
            var vendor2 = Guid.NewGuid();
            _context.Users.AddRange(
                new User { Id = vendor1, Email = "v1-scope@ex.com", PasswordHash = "x", PasswordSalt = "x" },
                new User { Id = vendor2, Email = "v2-scope@ex.com", PasswordHash = "x", PasswordSalt = "x" }
            );
            var date = DateTime.UtcNow.Date;

            _context.Invoices.AddRange(
                new Invoice { Id = Guid.NewGuid(), TotalAmount = 100, CreatedAt = date, UploadedByVendorId = vendor1, DriveFileId = "file-v1-1" },
                new Invoice { Id = Guid.NewGuid(), TotalAmount = 200, CreatedAt = date, UploadedByVendorId = vendor2, DriveFileId = "file-v2-1" }
            );
            await _context.SaveChangesAsync();

            // Act
            var result = await _sut.GetRevenueTrendDataAsync(date.AddDays(-1), date.AddDays(1), vendor1);

            // Assert
            Assert.Single(result);
            Assert.Equal(100, result[0].Amount);
        }

        [Fact]
        public async Task GetRevenueTrendDataAsync_AdminSeesAllVendors()
        {
            // Arrange
            var vendor1 = Guid.NewGuid();
            var vendor2 = Guid.NewGuid();
            _context.Users.AddRange(
                new User { Id = vendor1, Email = "v1-admin@ex.com", PasswordHash = "x", PasswordSalt = "x" },
                new User { Id = vendor2, Email = "v2-admin@ex.com", PasswordHash = "x", PasswordSalt = "x" }
            );
            var date = DateTime.UtcNow.Date;

            _context.Invoices.AddRange(
                new Invoice { Id = Guid.NewGuid(), TotalAmount = 100, CreatedAt = date, UploadedByVendorId = vendor1, DriveFileId = "file-admin-1" },
                new Invoice { Id = Guid.NewGuid(), TotalAmount = 200, CreatedAt = date, UploadedByVendorId = vendor2, DriveFileId = "file-admin-2" }
            );
            await _context.SaveChangesAsync();

            // Act
            var result = await _sut.GetRevenueTrendDataAsync(date.AddDays(-1), date.AddDays(1), null);

            // Assert
            Assert.Equal(2, result.Count);
        }
    }
}
