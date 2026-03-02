using invoice_v1.src.Api.Controllers;
using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace invoice_v1.tests.Controllers
{
    public class InvalidInvoiceControllerTests : ControllerTestBase
    {
        private readonly Mock<IInvalidInvoiceService> _mockInvalidInvoiceService;
        private readonly Mock<IJobService> _mockJobService;
        private readonly InvalidInvoiceController _sut;
        private readonly Guid _testVendorId = Guid.NewGuid();

        public InvalidInvoiceControllerTests()
        {
            _mockInvalidInvoiceService = new Mock<IInvalidInvoiceService>();
            _mockJobService = new Mock<IJobService>();
            _sut = new InvalidInvoiceController(_mockInvalidInvoiceService.Object, _mockJobService.Object);
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region Get (Invalid Invoices List)
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Get_AsVendor_PassesVendorIdToService()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var expectedResult = new { Data = new List<object>(), Total = 0 };

            _mockInvalidInvoiceService.Setup(s => s.GetInvalidInvoicesAsync(1, 20, _testVendorId))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _sut.Get(1, 20);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(expectedResult, okResult.Value);
            _mockInvalidInvoiceService.Verify(s => s.GetInvalidInvoicesAsync(1, 20, _testVendorId), Times.Once);
        }

        [Fact]
        public async Task Get_AsAdmin_PassesNullVendorId()
        {
            // Arrange
            SetupUser(_sut, Guid.NewGuid(), role: "Admin");
            var expectedResult = new { Data = new List<object>(), Total = 100 };

            _mockInvalidInvoiceService.Setup(s => s.GetInvalidInvoicesAsync(2, 50, null))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _sut.Get(2, 50);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockInvalidInvoiceService.Verify(s => s.GetInvalidInvoicesAsync(2, 50, null), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region Requeue (Admin Only)
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Requeue_Success_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, Guid.NewGuid(), role: "Admin");
            var jobId = Guid.NewGuid();

            // Act
            var result = await _sut.Requeue(jobId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            // Verify message content
            var messageProp = okResult.Value?.GetType().GetProperty("message");
            Assert.Equal("Job requeued successfully", messageProp?.GetValue(okResult.Value));

            _mockJobService.Verify(s => s.RequeueJobAsync(jobId), Times.Once);
        }

        [Fact]
        public async Task Requeue_ServiceThrows_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(_sut, Guid.NewGuid(), role: "Admin");
            var jobId = Guid.NewGuid();
            _mockJobService.Setup(s => s.RequeueJobAsync(jobId))
                .ThrowsAsync(new Exception("Database error"));

            // Act
            var result = await _sut.Requeue(jobId);

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            var messageProp = badRequest.Value?.GetType().GetProperty("message");
            Assert.Equal("Database error", messageProp?.GetValue(badRequest.Value));
        }

        #endregion
    }
}
