using invoice_v1.src.Api.Controllers;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Exceptions;
using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace invoice_v1.tests.Controllers
{
    public class VendorInvoicesControllerTests
    {
        private readonly Mock<IVendorInvoiceService> _mockVendorInvoiceService;
        private readonly Mock<ILogger<VendorInvoicesController>> _mockLogger;
        private readonly VendorInvoicesController _sut;
        private readonly Guid _vendorId = Guid.NewGuid();

        public VendorInvoicesControllerTests()
        {
            _mockVendorInvoiceService = new Mock<IVendorInvoiceService>();
            _mockLogger = new Mock<ILogger<VendorInvoicesController>>();
            _sut = new VendorInvoicesController(
                _mockVendorInvoiceService.Object,
                _mockLogger.Object);

            // Set up Vendor claims with both VendorId and NameIdentifier
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, _vendorId.ToString()),
                new(ClaimTypes.Role, "Vendor")
            };
            _sut.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            };
        }

        private IFormFile CreateMockFile(string name = "invoice.pdf", long size = 1024)
        {
            var stream = new MemoryStream(new byte[size]);
            return new FormFile(stream, 0, size, "file", name);
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region UploadInvoice
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task UploadInvoice_Success_ReturnsOk()
        {
            // Arrange
            var file = CreateMockFile();
            var request = new UploadInvoiceRequest { File = file };
            var uploadResult = new UploadResult { Success = true, FileId = "drive-abc123", Message = "Uploaded" };

            _mockVendorInvoiceService.Setup(s => s.UploadInvoiceAsync(_vendorId, file))
                .ReturnsAsync(uploadResult);

            // Act
            var result = await _sut.UploadInvoice(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(uploadResult, okResult.Value);
        }

        [Fact]
        public async Task UploadInvoice_NoFile_ReturnsBadRequest()
        {
            // Arrange
            var request = new UploadInvoiceRequest { File = null! };

            // Act
            var result = await _sut.UploadInvoice(request);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UploadInvoice_EmptyFile_ReturnsBadRequest()
        {
            // Arrange
            var file = CreateMockFile(size: 0);
            var request = new UploadInvoiceRequest { File = file };

            // Act
            var result = await _sut.UploadInvoice(request);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UploadInvoice_SecurityFailure_Returns422()
        {
            // Arrange
            var file = CreateMockFile();
            var request = new UploadInvoiceRequest { File = file };
            var uploadResult = new UploadResult
            {
                Success = false,
                Message = "File failed security check",
                SecurityReason = "Malware detected"
            };

            _mockVendorInvoiceService.Setup(s => s.UploadInvoiceAsync(_vendorId, file))
                .ReturnsAsync(uploadResult);

            // Act
            var result = await _sut.UploadInvoice(request);

            // Assert
            Assert.IsType<UnprocessableEntityObjectResult>(result);
        }

        [Fact]
        public async Task UploadInvoice_RateLimited_Returns429()
        {
            // Arrange
            var file = CreateMockFile();
            var request = new UploadInvoiceRequest { File = file };

            _mockVendorInvoiceService.Setup(s => s.UploadInvoiceAsync(_vendorId, file))
                .ThrowsAsync(new RateLimitExceededException("Too many uploads"));

            // Act
            var result = await _sut.UploadInvoice(request);

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(429, statusResult.StatusCode);
        }

        [Fact]
        public async Task UploadInvoice_InvalidArgument_ReturnsBadRequest()
        {
            // Arrange
            var file = CreateMockFile("virus.exe");
            var request = new UploadInvoiceRequest { File = file };

            _mockVendorInvoiceService.Setup(s => s.UploadInvoiceAsync(_vendorId, file))
                .ThrowsAsync(new ArgumentException("Unsupported file type"));

            // Act
            var result = await _sut.UploadInvoice(request);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UploadInvoice_VendorNotFound_ReturnsNotFound()
        {
            // Arrange
            var file = CreateMockFile();
            var request = new UploadInvoiceRequest { File = file };

            _mockVendorInvoiceService.Setup(s => s.UploadInvoiceAsync(_vendorId, file))
                .ThrowsAsync(new InvalidOperationException("Vendor not found"));

            // Act
            var result = await _sut.UploadInvoice(request);

            // Assert
            Assert.IsType<NotFoundObjectResult>(result);
        }

        #endregion
    }
}
