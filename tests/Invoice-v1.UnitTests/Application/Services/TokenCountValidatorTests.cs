using FluentAssertions;
using invoice_v1.src.Application.Security;
using invoice_v1.src.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Invoice_v1.UnitTests;

public class TokenCountValidatorTests
{
    private class TestTokenCountValidator : TokenCountValidator
    {
        private readonly int _fakePdfTokens;

        public TestTokenCountValidator(int maxTokens, int fakePdfTokens)
            : base(CreateConfiguration(maxTokens),
                   NullLogger<TokenCountValidator>.Instance)
        {
            _fakePdfTokens = fakePdfTokens;
        }

        protected override Task<int> EstimatePdfTokensAsync(IFormFile file)
        {
            return Task.FromResult(_fakePdfTokens);
        }

        private static IConfiguration CreateConfiguration(int maxTokens)
        {
            var settings = new Dictionary<string, string>
            {
                { "Security:MaxTokensAllowed", maxTokens.ToString() }
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(settings!)
                .Build();
        }
    }
    private async Task<IFormFile> CreateImageAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        ms.Position = 0;

        return new FormFile(ms, 0, ms.Length, "file", "image.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
    }

    [Fact]
    public async Task ValidateAsync_ShouldPass_ForPdfUnderLimit()
    {
        var validator = new TestTokenCountValidator(
            maxTokens: 100000,
            fakePdfTokens: 1000
        );

        var dummyFile = new FormFile(
            new MemoryStream(),
            0,
            0,
            "file",
            "test.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        await validator.Invoking(v => v.ValidateAsync(dummyFile))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrow_WhenPdfExceedsLimit()
    {
        var validator = new TestTokenCountValidator(
            maxTokens: 1000,
            fakePdfTokens: 10000
        );

        var dummyFile = new FormFile(
            new MemoryStream(),
            0,
            0,
            "file",
            "test.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        await validator.Invoking(v => v.ValidateAsync(dummyFile))
            .Should().ThrowAsync<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.TokenLimitExceeded);
    }


    [Fact]
    public async Task ValidateAsync_ShouldPass_ForImageUnderLimit()
    {
        var validator = new TestTokenCountValidator(
            maxTokens: 100000,
            fakePdfTokens: 0 // not used
        );

        var file = await CreateImageAsync(512, 512);

        await validator.Invoking(v => v.ValidateAsync(file))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrow_WhenImageExceedsLimit()
    {
        var validator = new TestTokenCountValidator(
            maxTokens: 100,
            fakePdfTokens: 0 // not used
        );

        var file = await CreateImageAsync(4000, 4000);

        await validator.Invoking(v => v.ValidateAsync(file))
            .Should().ThrowAsync<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.TokenLimitExceeded);
    }


    [Fact]
    public async Task ValidateAsync_ShouldThrow_ForUnsupportedType()
    {
        var validator = new TestTokenCountValidator(
            maxTokens: 100000,
            fakePdfTokens: 0
        );

        var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        var file = new FormFile(stream, 0, stream.Length, "file", "file.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        await validator.Invoking(v => v.ValidateAsync(file))
            .Should().ThrowAsync<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.UnsupportedType);
    }
}