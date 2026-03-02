using FluentAssertions;
using invoice_v1.src.Application.Security;
using invoice_v1.src.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Xunit;
using System.Text;

namespace Invoice_v1.UnitTests;

public class MagicBytesValidatorTests
{
    private readonly MagicBytesValidator _validator = new();

    private IFormFile CreateFile(byte[] content, string fileName, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }


    [Fact]
    public void Validate_ShouldPass_ForValidPdfMagic()
    {
        var content = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF-
        var file = CreateFile(content, "file.pdf", "application/pdf");

        _validator.Invoking(v => v.Validate(file))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldThrow_ForInvalidPdfMagic()
    {
        var content = Encoding.UTF8.GetBytes("NOTPDF");
        var file = CreateFile(content, "file.pdf", "application/pdf");

        _validator.Invoking(v => v.Validate(file))
            .Should().Throw<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.MagicBytesMismatch);
    }

    [Theory]
    [InlineData(0xE0)]
    [InlineData(0xE1)]
    [InlineData(0xE8)]
    public void Validate_ShouldPass_ForValidJpegVariants(byte variant)
    {
        var content = new byte[] { 0xFF, 0xD8, 0xFF, variant, 0x00 };
        var file = CreateFile(content, "image.jpg", "image/jpeg");

        _validator.Invoking(v => v.Validate(file))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldThrow_ForInvalidJpegMagic()
    {
        var content = new byte[] { 0xFF, 0x00, 0xFF, 0xE0 };
        var file = CreateFile(content, "image.jpg", "image/jpeg");

        _validator.Invoking(v => v.Validate(file))
            .Should().Throw<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.MagicBytesMismatch);
    }


    [Fact]
    public void Validate_ShouldPass_ForValidPngMagic()
    {
        var content = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A,
            0x00
        };

        var file = CreateFile(content, "image.png", "image/png");

        _validator.Invoking(v => v.Validate(file))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldThrow_ForInvalidPngMagic()
    {
        var content = Encoding.UTF8.GetBytes("NOTPNGDATA");
        var file = CreateFile(content, "image.png", "image/png");

        _validator.Invoking(v => v.Validate(file))
            .Should().Throw<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.MagicBytesMismatch);
    }

    [Fact]
    public void Validate_ShouldThrow_ForUnsupportedMimeType()
    {
        var content = Encoding.UTF8.GetBytes("dummy");
        var file = CreateFile(content, "file.txt", "text/plain");

        _validator.Invoking(v => v.Validate(file))
            .Should().Throw<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.UnsupportedType);
    }

    [Fact]
    public void Validate_ShouldThrow_WhenFileTooShort()
    {
        var content = new byte[] { 0x25 }; // Too short for PDF magic
        var file = CreateFile(content, "file.pdf", "application/pdf");

        _validator.Invoking(v => v.Validate(file))
            .Should().Throw<SecurityValidationException>()
            .Where(e => e.FailCode == SecurityFailReason.MagicBytesMismatch);
    }
}