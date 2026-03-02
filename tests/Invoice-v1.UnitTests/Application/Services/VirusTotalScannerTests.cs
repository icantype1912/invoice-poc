using FluentAssertions;
using invoice_v1.src.Application.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using Xunit;

namespace Invoice_v1.UnitTests;

public class VirusTotalScannerTests
{
    private static VirusTotalScanner CreateScanner(
        HttpResponseMessage response,
        string? apiKey = "test-api-key",
        Exception? exception = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>();

        if (exception != null)
        {
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(exception);
        }
        else
        {
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(response);
        }

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://www.virustotal.com")
        };

        var settings = new Dictionary<string, string?>
        {
            { "VirusTotal:ApiKey", apiKey }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings!)
            .Build();

        return new VirusTotalScanner(
            httpClient,
            configuration,
            NullLogger<VirusTotalScanner>.Instance);
    }

    private static MemoryStream CreateTestStream(string content)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }

    [Fact]
    public async Task ScanAsync_ShouldSkip_WhenApiKeyMissing()
    {
        var scanner = CreateScanner(
            new HttpResponseMessage(HttpStatusCode.OK),
            apiKey: null);

        using var stream = CreateTestStream("test");

        var result = await scanner.ScanAsync(stream, "file.txt");

        result.IsClean.Should().BeTrue();
        result.IsUnknown.Should().BeTrue();
        result.Message.Should().Contain("skipped");
    }

    [Fact]
    public async Task ScanAsync_ShouldTreatNotFoundAsClean()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var scanner = CreateScanner(response);

        using var stream = CreateTestStream("test");

        var result = await scanner.ScanAsync(stream, "file.txt");

        result.IsClean.Should().BeTrue();
        result.IsUnknown.Should().BeTrue();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task ScanAsync_ShouldReturnClean_WhenNoMalicious()
    {
        var json = """
        {
          "data": {
            "attributes": {
              "last_analysis_stats": {
                "malicious": 0,
                "suspicious": 1,
                "harmless": 60,
                "undetected": 10
              }
            }
          }
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var scanner = CreateScanner(response);

        using var stream = CreateTestStream("test");

        var result = await scanner.ScanAsync(stream, "file.txt");

        result.IsClean.Should().BeTrue();
        result.MaliciousEngines.Should().Be(0);
        result.SuspiciousEngines.Should().Be(1);
        result.TotalEngines.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ScanAsync_ShouldReturnFlagged_WhenMalicious()
    {
        var json = """
        {
          "data": {
            "attributes": {
              "last_analysis_stats": {
                "malicious": 5,
                "suspicious": 3,
                "harmless": 40
              }
            }
          }
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var scanner = CreateScanner(response);

        using var stream = CreateTestStream("test");

        var result = await scanner.ScanAsync(stream, "file.txt");

        result.IsClean.Should().BeFalse();
        result.MaliciousEngines.Should().Be(5);
        result.SuspiciousEngines.Should().Be(3);
    }

    [Fact]
    public async Task ScanAsync_ShouldFailOpen_OnNonSuccessStatus()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var scanner = CreateScanner(response);

        using var stream = CreateTestStream("test");

        var result = await scanner.ScanAsync(stream, "file.txt");

        result.IsClean.Should().BeTrue();
        result.Message.Should().Contain("failing open");
    }

    [Fact]
    public async Task ScanAsync_ShouldFailOpen_OnException()
    {
        var scanner = CreateScanner(
            new HttpResponseMessage(HttpStatusCode.OK),
            exception: new HttpRequestException("network failure"));

        using var stream = CreateTestStream("test");

        var result = await scanner.ScanAsync(stream, "file.txt");

        result.IsClean.Should().BeTrue();
        result.Message.Should().Contain("API error");
    }


    [Fact]
    public async Task ScanAsync_ShouldComputeConsistentHash()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var scanner = CreateScanner(response);

        using var stream1 = CreateTestStream("same-content");
        using var stream2 = CreateTestStream("same-content");

        var result1 = await scanner.ScanAsync(stream1, "file.txt");
        var result2 = await scanner.ScanAsync(stream2, "file.txt");

        result1.Hash.Should().Be(result2.Hash);
    }
}