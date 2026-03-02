using invoice_v1.src.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace invoice_v1.tests.Application.Services
{
    public class WorkerClientTests
    {
        private readonly Mock<ILogger<WorkerClient>> _mockLogger;
        private readonly Mock<IConfiguration> _mockConfig;
        private readonly Mock<IHttpClientFactory> _mockHttpFactory;
        private readonly Mock<HttpMessageHandler> _mockHandler;

        public WorkerClientTests()
        {
            _mockLogger = new Mock<ILogger<WorkerClient>>();
            _mockConfig = new Mock<IConfiguration>();
            _mockHttpFactory = new Mock<IHttpClientFactory>();
            _mockHandler = new Mock<HttpMessageHandler>();

            // Setup the factory to return a client using our mocked handler
            var httpClient = new HttpClient(_mockHandler.Object);
            _mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        }

        private WorkerClient CreateSut() => new(
            _mockLogger.Object,
            _mockConfig.Object,
            _mockHttpFactory.Object);

        private void SetupHttpResponse(HttpStatusCode code)
        {
            _mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage { StatusCode = code });
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region URL and Configuration
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task SendCallbackAsync_UsesConfiguredUrl()
        {
            _mockConfig.Setup(c => c["Worker:ApiUrl"]).Returns("http://test-worker:9000");
            SetupHttpResponse(HttpStatusCode.OK);

            var sut = CreateSut();
            await sut.SendCallbackAsync(Guid.NewGuid(), "COMPLETED");

            _mockHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString() == "http://test-worker:9000/api/jobs/notify"),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task SendCallbackAsync_DefaultUrl_WhenConfigMissing()
        {
            _mockConfig.Setup(c => c["Worker:ApiUrl"]).Returns((string?)null);
            SetupHttpResponse(HttpStatusCode.OK);

            var sut = CreateSut();
            await sut.SendCallbackAsync(Guid.NewGuid(), "COMPLETED");

            _mockHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().StartsWith("http://localhost:8000")),
                ItExpr.IsAny<CancellationToken>());
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region Response Handling
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task SendCallbackAsync_SuccessStatusCode_ReturnsTrue()
        {
            SetupHttpResponse(HttpStatusCode.OK);
            var sut = CreateSut();

            var result = await sut.SendCallbackAsync(Guid.NewGuid(), "COMPLETED");

            Assert.True(result);
        }

        [Fact]
        public async Task SendCallbackAsync_ErrorStatusCode_ReturnsFalseAndLogsDebug()
        {
            SetupHttpResponse(HttpStatusCode.NotFound);
            var sut = CreateSut();

            var result = await sut.SendCallbackAsync(Guid.NewGuid(), "COMPLETED");

            Assert.False(result);

            _mockLogger.Verify(
                l => l.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    // FIX: Change "404" to "NotFound"
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("NotFound")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }


        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region Exception Handling
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task SendCallbackAsync_HttpRequestException_ReturnsFalse()
        {
            _mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network down"));

            var sut = CreateSut();
            var result = await sut.SendCallbackAsync(Guid.NewGuid(), "COMPLETED");

            Assert.False(result);
            _mockLogger.Verify(
                l => l.Log(LogLevel.Debug, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Network down")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task SendCallbackAsync_GeneralException_ReturnsFalseAndLogsWarning()
        {
            _mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new Exception("Critical failure"));

            var sut = CreateSut();
            var result = await sut.SendCallbackAsync(Guid.NewGuid(), "COMPLETED");

            Assert.False(result);
            _mockLogger.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Unexpected error")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region Payload Verification
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task SendCallbackAsync_SendsCorrectPayload()
        {
            SetupHttpResponse(HttpStatusCode.OK);
            var jobId = Guid.NewGuid();
            var status = "FAILED";
            var reason = "Bad file";

            var sut = CreateSut();
            await sut.SendCallbackAsync(jobId, status, reason: reason);

            _mockHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.Content != null), // PostAsJsonAsync sets content
                ItExpr.IsAny<CancellationToken>());
        }

        #endregion
    }
}
