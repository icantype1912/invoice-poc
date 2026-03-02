using FluentAssertions;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Exceptions;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Services;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Repositories;
using invoice_v1.src.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Invoice_v1.UnitTests;

public class VendorInvoiceServiceTests
{
    private readonly Mock<IFileSecurityPipeline> _securityMock = new();
    private readonly Mock<IGoogleDriveService> _driveMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IFileChangeLogRepository> _logRepoMock = new();
    private readonly Mock<IRateLimitService> _rateLimitMock = new();
    private readonly Mock<IConfiguration> _configMock = new();
    private readonly Mock<ILogger<VendorInvoiceService>> _loggerMock = new();

    private VendorInvoiceService CreateService()
    {
        var sectionMock = new Mock<IConfigurationSection>();
        sectionMock.Setup(s => s.Value).Returns("20");

        _configMock
            .Setup(c => c.GetSection("Security:MaxUploadsPerHour"))
            .Returns(sectionMock.Object);

        return new VendorInvoiceService(
            _securityMock.Object,
            _driveMock.Object,
            _userRepoMock.Object,
            _logRepoMock.Object,
            _rateLimitMock.Object,
            _configMock.Object,
            _loggerMock.Object);
    }

    private static IFormFile CreateFakeFile(string name = "test.pdf", long length = 100)
    {
        var content = new byte[length];
        var stream = new MemoryStream(content);

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns(name);
        mockFile.Setup(f => f.Length).Returns(length);
        mockFile.Setup(f => f.ContentType).Returns("application/pdf");
        mockFile.Setup(f => f.OpenReadStream()).Returns(stream);

        return mockFile.Object;
    }

    [Fact]
    public async Task UploadInvoiceAsync_ShouldThrow_WhenFileIsNull()
    {
        var service = CreateService();

        Func<Task> act = async () =>
            await service.UploadInvoiceAsync(Guid.NewGuid(), null!);

        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("File is required");
    }

    [Fact]
    public async Task UploadInvoiceAsync_ShouldThrow_WhenFileIsEmpty()
    {
        var service = CreateService();
        var file = CreateFakeFile(length: 0);

        Func<Task> act = async () =>
            await service.UploadInvoiceAsync(Guid.NewGuid(), file);

        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("File is required");
    }

    [Fact]
    public async Task UploadInvoiceAsync_ShouldThrow_WhenRateLimited()
    {
        var vendorId = Guid.NewGuid();
        var file = CreateFakeFile();

        _rateLimitMock
            .Setup(r => r.IsRateLimitedAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        var service = CreateService();

        Func<Task> act = async () =>
            await service.UploadInvoiceAsync(vendorId, file);

        await act.Should()
            .ThrowAsync<RateLimitExceededException>();
    }

    [Fact]
    public async Task UploadInvoiceAsync_ShouldThrow_WhenVendorNotFound()
    {
        var vendorId = Guid.NewGuid();
        var file = CreateFakeFile();

        _rateLimitMock
            .Setup(r => r.IsRateLimitedAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

        _rateLimitMock
            .Setup(r => r.IncrementAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        _userRepoMock
            .Setup(u => u.GetByIdAsync(vendorId))
            .ReturnsAsync((User?)null);

        var service = CreateService();

        Func<Task> act = async () =>
            await service.UploadInvoiceAsync(vendorId, file);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Vendor not found");

        _driveMock.Verify(d => d.UploadFileAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadInvoiceAsync_ShouldReject_WhenSecurityPipelineUnhealthy()
    {
        var vendorId = Guid.NewGuid();
        var file = CreateFakeFile();

        var vendor = new User
        {
            Id = vendorId,
            Email = "vendor@test.com",
            IsSoftDeleted = false
        };

        _rateLimitMock
            .Setup(r => r.IsRateLimitedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

        _rateLimitMock
            .Setup(r => r.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        _userRepoMock
            .Setup(u => u.GetByIdAsync(vendorId))
            .ReturnsAsync(vendor);

        _securityMock
            .Setup(s => s.RunAsync(file, vendorId))
            .ReturnsAsync(new SecurityPipelineResult
            {
                IsHealthy = false,
                FailReason = "Virus detected"
            });

        _logRepoMock
            .Setup(l => l.GetRecentUnhealthyLogAsync(
                vendorId,
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync((FileChangeLog?)null);

        var service = CreateService();

        var result = await service.UploadInvoiceAsync(vendorId, file);

        result.Success.Should().BeFalse();
        result.SecurityReason.Should().Be("Virus detected");

        _driveMock.Verify(d => d.UploadFileAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            It.IsAny<string>()),
            Times.Never);

        _logRepoMock.Verify(l => l.CreateAsync(It.IsAny<FileChangeLog>()), Times.Once);
        _logRepoMock.Verify(l => l.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UploadInvoiceAsync_ShouldSkipLogCreation_WhenRecentUnhealthyLogExists()
    {
        var vendorId = Guid.NewGuid();
        var file = CreateFakeFile();

        var vendor = new User
        {
            Id = vendorId,
            Email = "vendor@test.com",
            IsSoftDeleted = false
        };

        _rateLimitMock
            .Setup(r => r.IsRateLimitedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

        _rateLimitMock
            .Setup(r => r.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        _userRepoMock
            .Setup(u => u.GetByIdAsync(vendorId))
            .ReturnsAsync(vendor);

        _securityMock
            .Setup(s => s.RunAsync(file, vendorId))
            .ReturnsAsync(new SecurityPipelineResult
            {
                IsHealthy = false,
                FailReason = "Virus detected"
            });

        // Simulate recent log already exists
        _logRepoMock
            .Setup(l => l.GetRecentUnhealthyLogAsync(
                vendorId,
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(new FileChangeLog());

        var service = CreateService();

        var result = await service.UploadInvoiceAsync(vendorId, file);

        result.Success.Should().BeFalse();
        result.SecurityReason.Should().Be("Virus detected");

        // Should NOT create new log
        _logRepoMock.Verify(l => l.CreateAsync(It.IsAny<FileChangeLog>()), Times.Never);
        _logRepoMock.Verify(l => l.SaveChangesAsync(), Times.Never);

        // Drive should never be called
        _driveMock.Verify(d => d.UploadFileAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadInvoiceAsync_ShouldUploadToDrive_WhenSecurityHealthy()
    {
        var vendorId = Guid.NewGuid();
        var file = CreateFakeFile("invoice.pdf");

        var vendor = new User
        {
            Id = vendorId,
            Email = "vendor@test.com",
            IsSoftDeleted = false
        };

        var driveResult = new DriveFileResult
        {
            Id = "drive-file-123",
            ModifiedTime = DateTime.UtcNow
        };

        _rateLimitMock
            .Setup(r => r.IsRateLimitedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

        _rateLimitMock
            .Setup(r => r.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        _userRepoMock
            .Setup(u => u.GetByIdAsync(vendorId))
            .ReturnsAsync(vendor);

        _securityMock
            .Setup(s => s.RunAsync(file, vendorId))
            .ReturnsAsync(new SecurityPipelineResult
            {
                IsHealthy = true,
                FailReason = null
            });

        _driveMock
            .Setup(d => d.UploadFileAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                vendor.Email))
            .ReturnsAsync(driveResult);

        var service = CreateService();

        var result = await service.UploadInvoiceAsync(vendorId, file);

        result.Success.Should().BeTrue();
        result.FileId.Should().Be("drive-file-123");
        result.Message.Should().Be("File uploaded and queued for processing.");

        _driveMock.Verify(d => d.UploadFileAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            vendor.Email),
            Times.Once);

        _logRepoMock.Verify(l => l.CreateAsync(It.Is<FileChangeLog>(log =>
            log.FileId == "drive-file-123" &&
            log.SecurityStatus == nameof(FileSecurityStatus.Healthy))),
            Times.Once);

        _logRepoMock.Verify(l => l.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UploadInvoiceAsync_ShouldThrow_WhenDriveUploadFails()
    {
        var vendorId = Guid.NewGuid();
        var file = CreateFakeFile("invoice.pdf");

        var vendor = new User
        {
            Id = vendorId,
            Email = "vendor@test.com",
            IsSoftDeleted = false
        };

        _rateLimitMock
            .Setup(r => r.IsRateLimitedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

        _rateLimitMock
            .Setup(r => r.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(Task.CompletedTask);

        _userRepoMock
            .Setup(u => u.GetByIdAsync(vendorId))
            .ReturnsAsync(vendor);

        _securityMock
            .Setup(s => s.RunAsync(file, vendorId))
            .ReturnsAsync(new SecurityPipelineResult
            {
                IsHealthy = true
            });

        _driveMock
            .Setup(d => d.UploadFileAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                vendor.Email))
            .ThrowsAsync(new Exception("Drive failure"));

        var service = CreateService();

        Func<Task> act = async () =>
            await service.UploadInvoiceAsync(vendorId, file);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("Drive failure");

        // Ensure no log was created
        _logRepoMock.Verify(l => l.CreateAsync(It.IsAny<FileChangeLog>()), Times.Never);
        _logRepoMock.Verify(l => l.SaveChangesAsync(), Times.Never);
    }
}