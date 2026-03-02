using FluentAssertions;
using invoice_v1.src.Application.Security;
using invoice_v1.src.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Xunit;
using System.Text;

namespace Invoice_v1.UnitTests;

public class FileTypeValidatorTests
{
    private readonly FileTypeValidator _validator = new();

    private IFormFile CreateFile(string fileName, string contentType)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("dummy"));
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    [Fact]
    public void Validate_ShouldPass_ForValidPdf()
    {
        var file = CreateFile("invoice.pdf", "application/pdf");

        _validator.Invoking(v => v.Validate(file))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldPass_ForValidJpeg()
    {
        var file = CreateFile("photo.jpg", "image/jpeg");

        _validator.Invoking(v => v.Validate(file))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldPass_ForValidPng()
    {
        var file = CreateFile("image.png", "image/png");

        _validator.Invoking(v => v.Validate(file))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldBeCaseInsensitive()
    {
        var file = CreateFile("IMAGE.JPEG", "image/jpeg");

        _validator.Invoking(v => v.Validate(file))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldThrow_ForInvalidMimeType()
    {
        var file = CreateFile("file.exe", "application/octet-stream");

        _validator.Invoking(v => v.Validate(file))
            .Should().Throw<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.InvalidMimeType);
    }

    [Fact]
    public void Validate_ShouldThrow_WhenContentTypeMissing()
    {
        var file = CreateFile("file.pdf", null!);

        _validator.Invoking(v => v.Validate(file))
            .Should().Throw<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.InvalidMimeType);
    }


    [Fact]
    public void Validate_ShouldThrow_WhenExtensionDoesNotMatchMime()
    {
        var file = CreateFile("file.png", "application/pdf");

        _validator.Invoking(v => v.Validate(file))
            .Should().Throw<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.MimeExtensionMismatch);
    }

    [Fact]
    public void Validate_ShouldThrow_WhenExtensionMissing()
    {
        var file = CreateFile("file", "application/pdf");

        _validator.Invoking(v => v.Validate(file))
            .Should().Throw<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.MimeExtensionMismatch);
    }
}