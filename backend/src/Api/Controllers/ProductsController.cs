using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace invoice_v1.src.Api.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    [ApiController]
    [Route("api/products")]
    public class ProductsController : BaseAuthenticatedController
    {
        private readonly IProductService _productService;
        private readonly ILogger<ProductsController> _logger;

        public ProductsController(
            IProductService productService,
            ILogger<ProductsController> logger)
        {
            _productService = productService;
            _logger = logger;
        }

        [HttpGet]
        [ProducesResponseType(typeof(ProductListResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProducts(
            [FromQuery] string? category = null,
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] Guid? vendorId = null)
        {
            var currentVendorId = GetVendorIdIfVendor();
            var filterId = IsAdmin ? vendorId : currentVendorId;

            var response = await _productService.GetProductsAsync(
                filterId,
                category,
                search,
                page,
                pageSize);

            return Ok(response);
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetProductById(Guid id)
        {
            var vendorId = GetVendorIdIfVendor();

            var product = await _productService.GetProductByIdAsync(id, vendorId);
            if (product == null)
            {
                return NotFound(new { message = $"Product with ID {id} not found" });
            }

            return Ok(product);
        }

        [HttpGet("by-code/{productId}")]
        [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetProductByProductId(string productId)
        {
            var vendorId = GetVendorIdIfVendor();

            var product = await _productService.GetProductByProductIdAsync(productId, vendorId);
            if (product == null)
            {
                return NotFound(new { message = $"Product with code {productId} not found" });
            }

            return Ok(product);
        }

        [HttpGet("categories")]
        [ProducesResponseType(typeof(List<CategoryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCategories()
        {
            var vendorId = GetVendorIdIfVendor();
            var categoryDtos = await _productService.GetCategoriesAsync(vendorId);
            return Ok(categoryDtos);
        }
    }
}
