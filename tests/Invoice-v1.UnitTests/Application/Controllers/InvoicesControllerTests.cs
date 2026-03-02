using invoice_v1.src.Api.Controllers;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace invoice_v1.tests.Controllers
{
    public class InvoicesControllerTests : ControllerTestBase
    {
        private readonly Mock<IInvoiceService> _mockInvoiceService;
        private readonly Mock<ILogger<InvoicesController>> _mockLogger;
        private readonly InvoicesController _sut;
        private readonly Guid _testVendorId = Guid.NewGuid();

        public InvoicesControllerTests()
        {
            _mockInvoiceService = new Mock<IInvoiceService>();
            _mockLogger = new Mock<ILogger<InvoicesController>>();
            _sut = new InvoicesController(_mockInvoiceService.Object, _mockLogger.Object);
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region GetInvoiceById
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetInvoiceById_VendorOwnsInvoice_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var invoiceId = Guid.NewGuid();
            var invoiceDto = new InvoiceDto { Id = invoiceId, UploadedByVendorId = _testVendorId };

            _mockInvoiceService.Setup(s => s.GetInvoiceByIdAsync(invoiceId))
                .ReturnsAsync(invoiceDto);

            // Act
            var result = await _sut.GetInvoiceById(invoiceId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(invoiceDto, okResult.Value);
        }

        [Fact]
        public async Task GetInvoiceById_VendorDoesNotOwnInvoice_ReturnsForbid()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var invoiceId = Guid.NewGuid();
            var otherVendorId = Guid.NewGuid();
            var invoiceDto = new InvoiceDto { Id = invoiceId, UploadedByVendorId = otherVendorId };

            _mockInvoiceService.Setup(s => s.GetInvoiceByIdAsync(invoiceId))
                .ReturnsAsync(invoiceDto);

            // Act
            var result = await _sut.GetInvoiceById(invoiceId);

            // Assert
            Assert.IsType<ForbidResult>(result);
            _mockLogger.Verify(
                x => x.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("attempted to access invoice")),
                    It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetInvoiceById_AdminAccess_ReturnsOkEvenIfOwnedByOther()
        {
            // Arrange
            SetupUser(_sut, Guid.NewGuid(), role: "Admin");
            var invoiceId = Guid.NewGuid();
            var invoiceDto = new InvoiceDto { Id = invoiceId, UploadedByVendorId = _testVendorId };

            _mockInvoiceService.Setup(s => s.GetInvoiceByIdAsync(invoiceId))
                .ReturnsAsync(invoiceDto);

            // Act
            var result = await _sut.GetInvoiceById(invoiceId);

            // Assert
            Assert.IsType<OkObjectResult>(result); // Admins can see everything
        }

        [Fact]
        public async Task GetInvoiceById_NotFound_ReturnsNotFound()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var invoiceId = Guid.NewGuid();
            _mockInvoiceService.Setup(s => s.GetInvoiceByIdAsync(invoiceId))
                .ReturnsAsync((InvoiceDto?)null);

            // Act
            var result = await _sut.GetInvoiceById(invoiceId);

            // Assert
            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Contains("not found", notFound.Value?.ToString() ?? "");
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetInvoiceByFileId
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetInvoiceByFileId_ValidFile_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            const string fileId = "drive-123";
            var invoiceDto = new InvoiceDto { Id = Guid.NewGuid(), UploadedByVendorId = _testVendorId };

            _mockInvoiceService.Setup(s => s.GetInvoiceByFileIdAsync(fileId))
                .ReturnsAsync(invoiceDto);

            // Act
            var result = await _sut.GetInvoiceByFileId(fileId);

            // Assert
            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetInvoices (Paginated List)
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetInvoices_ReturnsCorrectPagination()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var invoices = new List<InvoiceDto> { new(), new() };
            _mockInvoiceService.Setup(s => s.GetInvoicesAsync(_testVendorId, 1, 10))
                .ReturnsAsync((invoices, 25)); // Total 25 items

            // Act
            var result = await _sut.GetInvoices(1, 10);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<InvoiceListResponse>(okResult.Value);
            Assert.Equal(3, response.TotalPages); // ceil(25/10)
            Assert.Equal(2, response.Invoices.Count);
        }

        #endregion
    }
}
