using invoice_v1.src.Api.Controllers;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace invoice_v1.tests.Controllers
{
    public class JobsControllerTests : ControllerTestBase
    {
        private readonly Mock<IJobService> _mockJobService;
        private readonly Mock<IConfiguration> _mockConfig;
        private readonly Mock<ILogger<JobsController>> _mockLogger;
        private readonly JobsController _sut;
        private readonly Guid _testVendorId = Guid.NewGuid();

        public JobsControllerTests()
        {
            _mockJobService = new Mock<IJobService>();
            _mockConfig = new Mock<IConfiguration>();
            _mockLogger = new Mock<ILogger<JobsController>>();
            _sut = new JobsController(_mockJobService.Object, _mockConfig.Object, _mockLogger.Object);
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region GetJobs
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetJobs_ValidStatus_ReturnsOkWithResponse()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var jobsList = new List<JobDto> { new() { Id = Guid.NewGuid() } };
            _mockJobService.Setup(s => s.GetJobsAsync(JobStatus.COMPLETED, 1, 50, _testVendorId))
                .ReturnsAsync((jobsList, 1));

            // Act
            var result = await _sut.GetJobs("COMPLETED", 1, 50);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<JobListResponse>(okResult.Value);
            Assert.Single(response.Jobs);
            Assert.Equal(1, response.Total);
        }

        [Fact]
        public async Task GetJobs_InvalidStatus_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);

            // Act
            var result = await _sut.GetJobs("NOT_A_STATUS");

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Invalid status", badRequest.Value?.ToString() ?? "");
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetJobById (Access Control)
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetJobById_VendorWithAccess_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var jobId = Guid.NewGuid();
            var jobDto = new JobDto { Id = jobId };

            _mockJobService.Setup(s => s.GetJobByIdAsync(jobId)).ReturnsAsync(jobDto);
            _mockJobService.Setup(s => s.CanVendorAccessJobAsync(jobId, _testVendorId)).ReturnsAsync(true);

            // Act
            var result = await _sut.GetJobById(jobId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(jobDto, okResult.Value);
        }

        [Fact]
        public async Task GetJobById_VendorWithoutAccess_ReturnsForbid()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var jobId = Guid.NewGuid();

            _mockJobService.Setup(s => s.GetJobByIdAsync(jobId)).ReturnsAsync(new JobDto { Id = jobId });
            _mockJobService.Setup(s => s.CanVendorAccessJobAsync(jobId, _testVendorId)).ReturnsAsync(false);

            // Act
            var result = await _sut.GetJobById(jobId);

            // Assert
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GetJobById_Admin_BypassesAccessCheck()
        {
            // Arrange
            SetupUser(_sut, Guid.NewGuid(), role: "Admin");
            var jobId = Guid.NewGuid();
            var jobDto = new JobDto { Id = jobId };

            _mockJobService.Setup(s => s.GetJobByIdAsync(jobId)).ReturnsAsync(jobDto);

            // Act
            var result = await _sut.GetJobById(jobId);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            _mockJobService.Verify(s => s.CanVendorAccessJobAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region RequeueJob
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task RequeueJob_Success_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, Guid.NewGuid(), role: "Admin");
            var jobId = Guid.NewGuid();

            // Act
            var result = await _sut.RequeueJob(jobId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockJobService.Verify(s => s.RequeueJobAsync(jobId), Times.Once);
        }

        [Fact]
        public async Task RequeueJob_NotFoundException_ReturnsNotFound()
        {
            // Arrange
            SetupUser(_sut, Guid.NewGuid(), role: "Admin");
            var jobId = Guid.NewGuid();
            _mockJobService.Setup(s => s.RequeueJobAsync(jobId))
                .ThrowsAsync(new InvalidOperationException("Job not found"));

            // Act
            var result = await _sut.RequeueJob(jobId);

            // Assert
            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Contains("Job not found", notFound.Value?.ToString() ?? "");
        }

        [Fact]
        public async Task RequeueJob_GeneralException_Returns500()
        {
            // Arrange
            SetupUser(_sut, Guid.NewGuid(), role: "Admin");
            _mockJobService.Setup(s => s.RequeueJobAsync(It.IsAny<Guid>()))
                .ThrowsAsync(new Exception("Database down"));

            // Act
            var result = await _sut.RequeueJob(Guid.NewGuid());

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
        }

        #endregion
    }
}
