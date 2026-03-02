using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Services;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace invoice_v1.tests.Application.Services
{
    public class ProductServiceTests
    {
        private readonly Mock<IProductRepository> _mockRepo;
        private readonly Mock<ILogger<ProductService>> _mockLogger;
        private readonly ProductService _sut;

        public ProductServiceTests()
        {
            _mockRepo = new Mock<IProductRepository>();
            _mockLogger = new Mock<ILogger<ProductService>>();
            _sut = new ProductService(_mockRepo.Object, _mockLogger.Object);
        }

        private static Product BuildProduct(string productId = "P001") => new()
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ProductName = "Test Product",
            Category = "Test Cat",
            TotalQuantitySold = 10,
            TotalRevenue = 100m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-1, 1)]
        public async Task GetProductsAsync_InvalidPage_NormalizesTo1(int badPage, int expectedPage)
        {
            _mockRepo.Setup(r => r.GetProductCountAsync(null, null, null)).ReturnsAsync(0);
            _mockRepo.Setup(r => r.GetProductsAsync(null, null, null, 0, 50)).ReturnsAsync(new List<Product>());

            var result = await _sut.GetProductsAsync(null, null, null, badPage, 50);

            Assert.Equal(expectedPage, result.Page);
        }

        [Fact]
        public async Task GetProductByIdAsync_AccessDenied_ReturnsNullAndLogsWarning()
        {
            var product = BuildProduct("P99");
            var vendorId = Guid.NewGuid();
            _mockRepo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
            _mockRepo.Setup(r => r.CanVendorAccessProductAsync("P99", vendorId)).ReturnsAsync(false);

            var result = await _sut.GetProductByIdAsync(product.Id, vendorId);

            Assert.Null(result);
            _mockLogger.Verify(
                l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("P99")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetCategoriesAsync_MapsRepositoryResults()
        {
            var vendorId = Guid.NewGuid();

            // Correct Tuple type from IProductRepository
            var repoResult = new List<(string Category, int ProductCount, decimal TotalRevenue)>
            {
                ("Cat1", 5, 500m)
            };

            _mockRepo.Setup(r => r.GetCategoriesAsync(vendorId)).ReturnsAsync(repoResult);

            var result = await _sut.GetCategoriesAsync(vendorId);

            Assert.Single(result);
            Assert.Equal("Cat1", result[0].Category);
            Assert.Equal(5, result[0].ProductCount);
            Assert.Equal(500m, result[0].TotalRevenue);
        }
    }
}
