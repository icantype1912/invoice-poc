using invoice_v1.src.Domain.Enums;
using Microsoft.AspNetCore.Http;
using UglyToad.PdfPig;
using SixLabors.ImageSharp;

namespace invoice_v1.src.Application.Security
{
    public class TokenCountValidator
    {
        private readonly int _maxTokens;
        private readonly ILogger<TokenCountValidator> _logger;

        public TokenCountValidator(IConfiguration configuration, ILogger<TokenCountValidator> logger)
        {
            _maxTokens = configuration.GetValue<int>("Security:MaxTokensAllowed", 120000);
            _logger = logger;
        }

        public async Task ValidateAsync(IFormFile file)
        {
            var mimeType = file.ContentType?.Trim().ToLowerInvariant();
            int estimatedTokens;

            switch (mimeType)
            {
                case "application/pdf":
                    estimatedTokens = await EstimatePdfTokensAsync(file);
                    break;

                case "image/jpeg":
                case "image/png":
                    estimatedTokens = await EstimateImageTokensAsync(file);
                    break;

                default:
                    throw new SecurityValidationException(
                        $"Unsupported type for token estimation: {mimeType}",
                        SecurityFailReason.UnsupportedType);
            }

            _logger.LogInformation(
                "Estimated {TokenCount} tokens for file {FileName} (max: {MaxTokens})",
                estimatedTokens, file.FileName, _maxTokens);

            if (estimatedTokens > _maxTokens)
            {
                throw new SecurityValidationException(
                    $"Estimated token count ({estimatedTokens}) exceeds maximum allowed ({_maxTokens})",
                    SecurityFailReason.TokenLimitExceeded);
            }
        }

        protected virtual Task<int> EstimatePdfTokensAsync(IFormFile file)
        {
            using var stream = file.OpenReadStream();
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            using var document = PdfDocument.Open(memoryStream);
            int totalChars = 0;
            int pageCount = document.NumberOfPages;

            foreach (var page in document.GetPages())
            {
                var text = page.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    totalChars += text.Length;
                }
            }

            // Estimate: totalChars / 4 + pageCount * 100 for structure overhead
            int tokens = (totalChars / 4) + (pageCount * 100);
            return Task.FromResult(tokens);
        }

        protected virtual async Task<int> EstimateImageTokensAsync(IFormFile file)
        {
            using var stream = file.OpenReadStream();
            using var image = await Image.LoadAsync(stream);

            int width = image.Width;
            int height = image.Height;

            // Scale down if width or height > 2048
            if (width > 2048 || height > 2048)
            {
                double scale = 2048.0 / Math.Max(width, height);
                width = (int)(width * scale);
                height = (int)(height * scale);
            }

            // Scale so shortest side <= 768
            int shortestSide = Math.Min(width, height);
            if (shortestSide > 768)
            {
                double scale = 768.0 / shortestSide;
                width = (int)(width * scale);
                height = (int)(height * scale);
            }

            // Count 512x512 tiles
            int tilesX = (int)Math.Ceiling(width / 512.0);
            int tilesY = (int)Math.Ceiling(height / 512.0);
            int totalTiles = tilesX * tilesY;

            // OpenAI vision token formula
            int tokens = 85 + (totalTiles * 170);
            return tokens;
        }
    }
}
