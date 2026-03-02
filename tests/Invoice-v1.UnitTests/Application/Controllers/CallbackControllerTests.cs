using invoice_v1.src.Api.Controllers;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace invoice_v1.tests.Controllers
{
    public class CallbackControllerTests
    {
        private readonly Mock<IJobService> _mockJobService;
        private readonly Mock<IInvoiceService> _mockInvoiceService;
        private readonly Mock<IHmacValidator> _mockHmacValidator;
        private readonly Mock<ILogger<CallbackController>> _mockLogger;
        private readonly CallbackController _sut;

        public CallbackControllerTests()
        {
            _mockJobService = new Mock<IJobService>();
            _mockInvoiceService = new Mock<IInvoiceService>();
            _mockHmacValidator = new Mock<IHmacValidator>();
            _mockLogger = new Mock<ILogger<CallbackController>>();
            _sut = new CallbackController(
                _mockJobService.Object,
                _mockInvoiceService.Object,
                _mockHmacValidator.Object,
                _mockLogger.Object);
        }

        private void SetupHttpContext(string body, string? hmac = "valid-hmac")
        {
            var context = new DefaultHttpContext();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));

            context.Request.Body = stream;
            context.Request.ContentLength = stream.Length;
            context.Request.ContentType = "application/json";

            if (hmac != null)
            {
                context.Request.Headers["X-Callback-HMAC"] = hmac;
            }

            _sut.ControllerContext = new ControllerContext
            {
                HttpContext = context
            };
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region Security and Validation
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task HandleCallback_MissingHmacHeader_ReturnsUnauthorized()
        {
            SetupHttpContext("{}", hmac: null);

            var result = await _sut.HandleCallback();

            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public async Task HandleCallback_InvalidHmac_ReturnsUnauthorized()
        {
            SetupHttpContext("{}", hmac: "wrong-signature");
            _mockHmacValidator.Setup(v => v.ValidateHmac(It.IsAny<string>(), "wrong-signature")).Returns(false);

            var result = await _sut.HandleCallback();

            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public async Task HandleCallback_InvalidJson_ReturnsBadRequest400()
        {
            // Arrange
            SetupHttpContext("this-is-not-json");
            _mockHmacValidator.Setup(v => v.ValidateHmac(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

            // Act
            var result = await _sut.HandleCallback();

            // Assert: Controller catches JsonException and returns BadRequest (400)
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(400, badRequestResult.StatusCode);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region Processing Logic
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task HandleCallback_JobAlreadyProcessed_ReturnsOkIdempotent()
        {
            // Arrange
            var jobId = Guid.NewGuid();
            var payload = new { JobId = jobId, Status = "COMPLETED" };
            var json = JsonSerializer.Serialize(payload);

            SetupHttpContext(json);
            _mockHmacValidator.Setup(v => v.ValidateHmac(json, "valid-hmac")).Returns(true);

            // Job is already in a final state
            _mockJobService.Setup(s => s.GetJobByIdAsync(jobId))
                .ReturnsAsync(new JobDto { Id = jobId, Status = "COMPLETED" });

            // Act
            var result = await _sut.HandleCallback();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Contains("already processed", okResult.Value?.ToString() ?? "");
        }

        [Fact]
        public async Task HandleCallback_StatusCompleted_TriggersInvoiceCreation()
        {
            // Arrange
            var jobId = Guid.NewGuid();
            var payload = new
            {
                JobId = jobId,
                Status = "COMPLETED",
                Result = new { InvoiceNumber = "INV-2026-001" }
            };
            var json = JsonSerializer.Serialize(payload);

            SetupHttpContext(json);
            _mockHmacValidator.Setup(v => v.ValidateHmac(json, "valid-hmac")).Returns(true);
            _mockJobService.Setup(s => s.GetJobByIdAsync(jobId)).ReturnsAsync(new JobDto { Id = jobId, Status = "PENDING" });

            // Act
            var result = await _sut.HandleCallback();

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockInvoiceService.Verify(s => s.CreateOrUpdateInvoiceFromCallbackAsync(jobId, It.IsAny<JsonElement>()), Times.Once);
            _mockJobService.Verify(s => s.CompleteJobAsync(jobId), Times.Once);
        }

        [Fact]
        public async Task HandleCallback_StatusFailed_MarksJobAsFailed()
        {
            // Arrange
            var jobId = Guid.NewGuid();
            var payload = new { JobId = jobId, Status = "FAILED", Reason = "Extraction Engine Error" };
            var json = JsonSerializer.Serialize(payload);

            SetupHttpContext(json);
            _mockHmacValidator.Setup(v => v.ValidateHmac(json, "valid-hmac")).Returns(true);
            _mockJobService.Setup(s => s.GetJobByIdAsync(jobId)).ReturnsAsync(new JobDto { Id = jobId, Status = "PENDING" });

            // Act
            var result = await _sut.HandleCallback();

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockJobService.Verify(s => s.MarkFailedAsync(jobId, It.IsAny<JsonDocument>()), Times.Once);
        }

        [Fact]
        public async Task HandleCallback_GeneralException_ReturnsInternalServerError500()
        {
            // Arrange: Valid JSON to pass parsing, but service throws
            var jobId = Guid.NewGuid();
            var json = JsonSerializer.Serialize(new { JobId = jobId, Status = "COMPLETED", Result = new { } });
            SetupHttpContext(json);

            _mockHmacValidator.Setup(v => v.ValidateHmac(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

            // Force the service to crash to trigger the final catch(Exception) block
            _mockJobService.Setup(s => s.GetJobByIdAsync(jobId)).ThrowsAsync(new Exception("Database connection lost"));

            // Act
            var result = await _sut.HandleCallback();

            // Assert: Generic catch block returns a 500 ObjectResult
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
        }

        #endregion
    }
}
