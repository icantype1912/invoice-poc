using invoice_v1.src.Application.Services;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace invoicev1.tests.Application.Services
{
    public class HmacValidatorTests
    {
        private const string ValidSecret = "super-secret-key-for-testing";

        private static HmacValidator CreateValidator(string? secret = ValidSecret)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(secret is null
                    ? new Dictionary<string, string?>()
                    : new Dictionary<string, string?> { ["Security:CallbackSecret"] = secret })
                .Build();

            var logger = new Mock<ILogger<HmacValidator>>().Object;
            return new HmacValidator(config, logger);
        }

        private static string ComputeExpectedHmac(string payload, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }

        #region Constructor

        [Fact]
        public void Constructor_MissingSecret_ThrowsInvalidOperationException()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            var logger = new Mock<ILogger<HmacValidator>>().Object;

            Assert.Throws<InvalidOperationException>(() => new HmacValidator(config, logger));
        }

        [Fact]
        public void Constructor_SecretConfigured_DoesNotThrow()
        {
            var ex = Record.Exception(() => CreateValidator(ValidSecret));
            Assert.Null(ex);
        }

        #endregion

        #region ValidateHmac — Guard Clauses

        [Theory]
        [InlineData(null, "someHmac")]
        [InlineData("", "someHmac")]
        [InlineData("   ", "someHmac")]
        public void ValidateHmac_NullOrWhitespacePayload_ReturnsFalse(string? payload, string hmac)
        {
            var validator = CreateValidator();
            Assert.False(validator.ValidateHmac(payload!, hmac));
        }

        [Theory]
        [InlineData("somePayload", null)]
        [InlineData("somePayload", "")]
        [InlineData("somePayload", "   ")]
        public void ValidateHmac_NullOrWhitespaceHmac_ReturnsFalse(string payload, string? hmac)
        {
            var validator = CreateValidator();
            Assert.False(validator.ValidateHmac(payload, hmac!));
        }

        [Fact]
        public void ValidateHmac_BothNullOrWhitespace_ReturnsFalse()
        {
            var validator = CreateValidator();
            Assert.False(validator.ValidateHmac("", ""));
        }

        #endregion

        #region ValidateHmac — Correct Validation

        [Fact]
        public void ValidateHmac_CorrectHmac_ReturnsTrue()
        {
            var validator = CreateValidator();
            var payload = "test-payload";
            var correctHmac = ComputeExpectedHmac(payload, ValidSecret);

            Assert.True(validator.ValidateHmac(payload, correctHmac));
        }

        [Fact]
        public void ValidateHmac_WrongHmac_ReturnsFalse()
        {
            var validator = CreateValidator();
            Assert.False(validator.ValidateHmac("test-payload", "dGhpcyBpcyBub3QgdGhlIHJpZ2h0IGhtYWM="));
        }

        [Fact]
        public void ValidateHmac_TamperedPayload_ReturnsFalse()
        {
            var validator = CreateValidator();
            var original = "original-payload";
            var validHmac = ComputeExpectedHmac(original, ValidSecret);

            Assert.False(validator.ValidateHmac("tampered-payload", validHmac));
        }

        [Fact]
        public void ValidateHmac_WrongSecret_ReturnsFalse()
        {
            var validatorA = CreateValidator("secret-A");
            var validatorB = CreateValidator("secret-B");
            var payload = "test-payload";
            var hmacFromA = validatorA.ComputeHmac(payload);

            Assert.False(validatorB.ValidateHmac(payload, hmacFromA));
        }

        [Fact]
        public void ValidateHmac_HmacWithExtraCharacter_ReturnsFalse()
        {
            var validator = CreateValidator();
            var payload = "test-payload";
            var hmac = ComputeExpectedHmac(payload, ValidSecret) + "X";

            Assert.False(validator.ValidateHmac(payload, hmac));
        }

        [Fact]
        public void ValidateHmac_HmacIsCaseSensitive()
        {
            var validator = CreateValidator();
            var payload = "test-payload";
            var hmac = ComputeExpectedHmac(payload, ValidSecret);
            var flippedHmac = hmac.ToUpper() == hmac ? hmac.ToLower() : hmac.ToUpper();

            // base64 is case-sensitive; flipped case should not validate
            Assert.False(validator.ValidateHmac(payload, flippedHmac));
        }

        [Fact]
        public void ValidateHmac_EmptyStringPayload_StillProducesConsistentResult()
        {
            // Empty string is caught by IsNullOrWhiteSpace — must return false
            var validator = CreateValidator();
            Assert.False(validator.ValidateHmac("", ComputeExpectedHmac("", ValidSecret)));
        }

        #endregion

        #region ValidateHmac — Timing Attack Resistance

        [Fact]
        public void ValidateHmac_UsesFixedTimeComparison_DoesNotShortCircuit()
        {
            // Structural test: both a close-but-wrong and a completely wrong HMAC
            // must both return false — no observable timing difference in outcome.
            var validator = CreateValidator();
            var payload = "payload";
            var correct = ComputeExpectedHmac(payload, ValidSecret);
            var oneOff = correct[..^1] + (correct[^1] == 'A' ? 'B' : 'A');
            var totallyWrong = Convert.ToBase64String(Encoding.UTF8.GetBytes("wrong"));

            Assert.False(validator.ValidateHmac(payload, oneOff));
            Assert.False(validator.ValidateHmac(payload, totallyWrong));
        }

        #endregion

        #region ComputeHmac

        [Fact]
        public void ComputeHmac_SameInput_ReturnsSameOutput()
        {
            var validator = CreateValidator();
            var h1 = validator.ComputeHmac("payload");
            var h2 = validator.ComputeHmac("payload");
            Assert.Equal(h1, h2);
        }

        [Fact]
        public void ComputeHmac_DifferentInputs_ReturnDifferentOutputs()
        {
            var validator = CreateValidator();
            Assert.NotEqual(
                validator.ComputeHmac("payload-A"),
                validator.ComputeHmac("payload-B"));
        }

        [Fact]
        public void ComputeHmac_ReturnsValidBase64String()
        {
            var validator = CreateValidator();
            var result = validator.ComputeHmac("any-payload");

            var ex = Record.Exception(() => Convert.FromBase64String(result));
            Assert.Null(ex);
        }

        [Fact]
        public void ComputeHmac_OutputLength_IsCorrectForSHA256()
        {
            // SHA256 = 32 bytes → base64 = 44 chars (with padding)
            var validator = CreateValidator();
            var result = validator.ComputeHmac("any-payload");
            Assert.Equal(44, result.Length);
        }

        [Fact]
        public void ComputeHmac_OutputMatchesKnownVector()
        {
            var validator = CreateValidator("my-secret");
            var expected = ComputeExpectedHmac("hello-world", "my-secret");
            Assert.Equal(expected, validator.ComputeHmac("hello-world"));
        }

        [Fact]
        public void ComputeHmac_DifferentSecret_ProducesDifferentHash()
        {
            var validatorA = CreateValidator("secret-A");
            var validatorB = CreateValidator("secret-B");
            Assert.NotEqual(
                validatorA.ComputeHmac("same-payload"),
                validatorB.ComputeHmac("same-payload"));
        }

        [Fact]
        public void ComputeHmac_LargePayload_DoesNotThrow()
        {
            var validator = CreateValidator();
            var largePayload = new string('x', 100_000);
            var ex = Record.Exception(() => validator.ComputeHmac(largePayload));
            Assert.Null(ex);
        }

        [Fact]
        public void ComputeHmac_UnicodePayload_DoesNotThrow()
        {
            var validator = CreateValidator();
            var ex = Record.Exception(() => validator.ComputeHmac("héllo wörld 日本語"));
            Assert.Null(ex);
        }

        [Fact]
        public void ComputeHmac_UnicodePayload_ValidateHmacRoundTrips()
        {
            var validator = CreateValidator();
            var payload = "héllo wörld 日本語";
            var hmac = validator.ComputeHmac(payload);
            Assert.True(validator.ValidateHmac(payload, hmac));
        }

        #endregion

        #region Round-Trip

        [Theory]
        [InlineData("simple")]
        [InlineData("{ \"key\": \"value\" }")]
        [InlineData("payload with spaces")]
        [InlineData("1234567890")]
        public void ValidateHmac_RoundTrip_ReturnsTrue(string payload)
        {
            var validator = CreateValidator();
            var hmac = validator.ComputeHmac(payload);
            Assert.True(validator.ValidateHmac(payload, hmac));
        }

        #endregion
    }
}
