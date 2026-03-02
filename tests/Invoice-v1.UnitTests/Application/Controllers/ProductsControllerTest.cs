using invoice_v1.src.Api.Controllers;
using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace invoice_v1.tests.Controllers
{
    public class ProductsControllerTests : ControllerTestBase
    {
        private readonly Mock<IProductService> _mockProductService;
        private readonly Mock<ILogger<ProductsController>> _mockLogger;
        private readonly ProductsController _sut;
        private readonly Guid _testVendorId = Guid.NewGuid();

        public ProductsControllerTests()
        {
            _mockProductService = new Mock<IProductService>();
            _mockLogger = new Mock<ILogger<ProductsController>>();
            _sut = new ProductsController(_mockProductService.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task GetProducts_AsVendor_PassesVendorIdToService()
        {
            // Arrange
            SetupUser(_sut, _testVendorId, role: "Vendor");
            var expectedResponse = new ProductListResponse { Products = new List<ProductDto>(), Total = 0 };

            _mockProductService.Setup(s => s.GetProductsAsync(_testVendorId, "Electronic", "Phone", 1, 50))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _sut.GetProducts("Electronic", "Phone", 1, 50);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(expectedResponse, okResult.Value);
            _mockProductService.Verify(s => s.GetProductsAsync(_testVendorId, "Electronic", "Phone", 1, 50), Times.Once);
        }

        [Fact]
        public async Task GetProducts_AsAdmin_PassesNullVendorIdToService()
        {
            // Arrange
            SetupUser(_sut, Guid.NewGuid(), role: "Admin"); // Admins should see all (null vendorId)
            var expectedResponse = new ProductListResponse { Products = new List<ProductDto>(), Total = 10 };

            _mockProductService.Setup(s => s.GetProductsAsync(null, null, null, 1, 50))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _sut.GetProducts();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockProductService.Verify(s => s.GetProductsAsync(null, null, null, 1, 50), Times.Once);
        }

        [Fact]
        public async Task GetProductById_ProductExists_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var productId = Guid.NewGuid();
            var productDto = new ProductDto { Id = productId, ProductName = "Test Product" };

            _mockProductService.Setup(s => s.GetProductByIdAsync(productId, _testVendorId))
                .ReturnsAsync(productDto);

            // Act
            var result = await _sut.GetProductById(productId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(productDto, okResult.Value);
        }

        [Fact]
        public async Task GetProductById_ProductNotFound_ReturnsNotFound()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var productId = Guid.NewGuid();

            _mockProductService.Setup(s => s.GetProductByIdAsync(productId, _testVendorId))
                .ReturnsAsync((ProductDto?)null);

            // Act
            var result = await _sut.GetProductById(productId);

            // Assert
            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            // Verify the anonymous object message
            var messageProp = notFoundResult.Value?.GetType().GetProperty("message");
            Assert.Contains(productId.ToString(), messageProp?.GetValue(notFoundResult.Value)?.ToString() ?? "");
        }

        [Fact]
        public async Task GetProductByProductId_ValidCode_ReturnsOk()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            const string productCode = "PRD-001";
            var productDto = new ProductDto { ProductId = productCode, ProductName = "Test Product" };

            _mockProductService.Setup(s => s.GetProductByProductIdAsync(productCode, _testVendorId))
                .ReturnsAsync(productDto);

            // Act
            var result = await _sut.GetProductByProductId(productCode);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(productDto, okResult.Value);
        }

        [Fact]
        public async Task GetCategories_ReturnsList()
        {
            // Arrange
            SetupUser(_sut, _testVendorId);
            var categories = new List<CategoryDto> { new() { Category = "Tools", ProductCount = 5 } };

            _mockProductService.Setup(s => s.GetCategoriesAsync(_testVendorId))
                .ReturnsAsync(categories);

            // Act
            var result = await _sut.GetCategories();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(categories, okResult.Value);
            _mockProductService.Verify(s => s.GetCategoriesAsync(_testVendorId), Times.Once);
        }
    }
}
