using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace invoice_v1.tests.Application.Services
{
    public class RateLimitServiceTests
    {
        private readonly Mock<IDistributedCache> _mockCache;
        private readonly Mock<ILogger<RateLimitService>> _mockLogger;
        private readonly RateLimitService _sut;

        public RateLimitServiceTests()
        {
            _mockCache = new Mock<IDistributedCache>();
            _mockLogger = new Mock<ILogger<RateLimitService>>();
            _sut = new RateLimitService(_mockCache.Object, _mockLogger.Object);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────

        private void SetupCacheGet(string key, object? data)
        {
            if (data == null)
            {
                _mockCache.Setup(c => c.GetAsync(key, default))
                          .ReturnsAsync((byte[]?)null);
            }
            else
            {
                var json = JsonSerializer.Serialize(data);
                _mockCache.Setup(c => c.GetAsync(key, default))
                          .ReturnsAsync(Encoding.UTF8.GetBytes(json));
            }
        }

        private void VerifyLogger(LogLevel level, string messagePart, Times times)
        {
            _mockLogger.Verify(
                x => x.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(messagePart)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                times);
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region IsRateLimitedAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task IsRateLimitedAsync_KeyNotFound_ReturnsFalse()
        {
            SetupCacheGet("test-key", null);
            var result = await _sut.IsRateLimitedAsync("test-key", 5, TimeSpan.FromMinutes(1));
            Assert.False(result);
        }

        [Fact]
        public async Task IsRateLimitedAsync_ResetTimePassed_ResetsAndReturnsFalse()
        {
            var expiredData = new { Attempts = 10, ResetTime = DateTime.UtcNow.AddMinutes(-1) };
            SetupCacheGet("test-key", expiredData);

            var result = await _sut.IsRateLimitedAsync("test-key", 5, TimeSpan.FromMinutes(1));

            Assert.False(result);
            _mockCache.Verify(c => c.RemoveAsync("test-key", default), Times.Once);
        }

        [Fact]
        public async Task IsRateLimitedAsync_UnderLimit_ReturnsFalse()
        {
            var data = new { Attempts = 3, ResetTime = DateTime.UtcNow.AddMinutes(1) };
            SetupCacheGet("test-key", data);

            var result = await _sut.IsRateLimitedAsync("test-key", 5, TimeSpan.FromMinutes(1));
            Assert.False(result);
        }

        [Fact]
        public async Task IsRateLimitedAsync_AtLimit_ReturnsTrueAndLogsWarning()
        {
            var data = new { Attempts = 5, ResetTime = DateTime.UtcNow.AddMinutes(1) };
            SetupCacheGet("test-key", data);

            var result = await _sut.IsRateLimitedAsync("test-key", 5, TimeSpan.FromMinutes(1));

            Assert.True(result);
            VerifyLogger(LogLevel.Warning, "Rate limit exceeded", Times.Once());
        }

        [Fact]
        public async Task IsRateLimitedAsync_CacheException_FailsOpenAndReturnsFalse()
        {
            _mockCache.Setup(c => c.GetAsync(It.IsAny<string>(), default))
                      .ThrowsAsync(new Exception("Cache down"));

            var result = await _sut.IsRateLimitedAsync("test-key", 5, TimeSpan.FromMinutes(1));

            Assert.False(result);
            VerifyLogger(LogLevel.Error, "Error checking rate limit", Times.Once());
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region IncrementAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task IncrementAsync_NewKey_CreatesEntryWithAttemptOne()
        {
            SetupCacheGet("new-key", null);
            byte[]? capturedData = null;

            _mockCache.Setup(c => c.SetAsync("new-key", It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), default))
                      .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((k, v, o, t) => capturedData = v)
                      .Returns(Task.CompletedTask);

            await _sut.IncrementAsync("new-key", TimeSpan.FromMinutes(10));

            var json = Encoding.UTF8.GetString(capturedData!);
            var data = JsonDocument.Parse(json);
            Assert.Equal(1, data.RootElement.GetProperty("Attempts").GetInt32());
            _mockCache.Verify(c => c.SetAsync("new-key", It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), default), Times.Once);
        }

        [Fact]
        public async Task IncrementAsync_ExistingKey_IncrementsAttempts()
        {
            var existing = new { Attempts = 2, ResetTime = DateTime.UtcNow.AddMinutes(5) };
            SetupCacheGet("existing-key", existing);
            byte[]? capturedData = null;

            _mockCache.Setup(c => c.SetAsync("existing-key", It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), default))
                      .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((k, v, o, t) => capturedData = v)
                      .Returns(Task.CompletedTask);

            await _sut.IncrementAsync("existing-key", TimeSpan.FromMinutes(10));

            var json = Encoding.UTF8.GetString(capturedData!);
            var data = JsonDocument.Parse(json);
            Assert.Equal(3, data.RootElement.GetProperty("Attempts").GetInt32());
        }

        [Fact]
        public async Task IncrementAsync_ExpiredKey_ResetsToAttemptOne()
        {
            var expired = new { Attempts = 5, ResetTime = DateTime.UtcNow.AddMinutes(-1) };
            SetupCacheGet("expired-key", expired);
            byte[]? capturedData = null;

            _mockCache.Setup(c => c.SetAsync("expired-key", It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), default))
                      .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((k, v, o, t) => capturedData = v)
                      .Returns(Task.CompletedTask);

            await _sut.IncrementAsync("expired-key", TimeSpan.FromMinutes(10));

            var json = Encoding.UTF8.GetString(capturedData!);
            var data = JsonDocument.Parse(json);
            Assert.Equal(1, data.RootElement.GetProperty("Attempts").GetInt32());
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region ResetAsync & GetAttemptsAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ResetAsync_CallsRemoveOnCache()
        {
            await _sut.ResetAsync("reset-key");
            _mockCache.Verify(c => c.RemoveAsync("reset-key", default), Times.Once);
        }

        [Fact]
        public async Task GetAttemptsAsync_KeyExists_ReturnsCorrectCount()
        {
            var data = new { Attempts = 7, ResetTime = DateTime.UtcNow.AddMinutes(1) };
            SetupCacheGet("count-key", data);

            var attempts = await _sut.GetAttemptsAsync("count-key");
            Assert.Equal(7, attempts);
        }

        [Fact]
        public async Task GetAttemptsAsync_KeyMissing_ReturnsZero()
        {
            SetupCacheGet("missing-key", null);
            var attempts = await _sut.GetAttemptsAsync("missing-key");
            Assert.Equal(0, attempts);
        }

        #endregion
    }
}
