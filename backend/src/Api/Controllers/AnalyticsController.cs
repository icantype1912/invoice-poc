using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace invoice_v1.src.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/analytics")]
    public class AnalyticsController : BaseAuthenticatedController
    {
        private readonly IAnalyticsService _analyticsService;
        private readonly ILogger<AnalyticsController> _logger;

        public AnalyticsController(
            IAnalyticsService analyticsService,
            ILogger<AnalyticsController> logger)
        {
            _analyticsService = analyticsService;
            _logger = logger;
        }

        [HttpGet("products/sales")]
        [ProducesResponseType(typeof(List<ProductSalesDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProductSales(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] string? category = null,
            [FromQuery] Guid? vendorId = null)
        {
            if (startDate > endDate)
            {
                return BadRequest(new { error = "startDate cannot be after endDate" });
            }

            if (endDate > DateTime.UtcNow.AddDays(1))
            {
                return BadRequest(new { error = "endDate cannot be in the future" });
            }

            var currentVendorId = GetVendorIdIfVendor();
            var filterId = IsAdmin ? vendorId : currentVendorId;

            var results = await _analyticsService.GetProductSalesByDateRangeAsync(
                startDate,
                endDate,
                category,
                filterId);

            return Ok(results);
        }

        [HttpGet("products/trending")]
        [ProducesResponseType(typeof(List<ProductTrendDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetTrendingProducts(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] int topN = 10,
            [FromQuery] Guid? vendorId = null)
        {
            if (startDate > endDate)
            {
                return BadRequest(new { error = "startDate cannot be after endDate" });
            }

            if (endDate > DateTime.UtcNow.AddDays(1))
            {
                return BadRequest(new { error = "endDate cannot be in the future" });
            }

            if (topN < 1)
            {
                return BadRequest(new { error = "topN must be at least 1" });
            }

            if (topN > 100)
            {
                return BadRequest(new { error = "topN cannot exceed 100" });
            }

            var currentVendorId = GetVendorIdIfVendor();
            var filterId = IsAdmin ? vendorId : currentVendorId;

            var results = await _analyticsService.GetTrendingProductsAsync(
                startDate,
                endDate,
                topN,
                filterId);

            return Ok(results);
        }

        [HttpGet("categories/sales")]
        [ProducesResponseType(typeof(List<CategorySalesDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCategorySales(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] Guid? vendorId = null)
        {
            if (startDate > endDate)
            {
                return BadRequest(new { error = "startDate cannot be after endDate" });
            }

            if (endDate > DateTime.UtcNow.AddDays(1))
            {
                return BadRequest(new { error = "endDate cannot be in the future" });
            }

            var currentVendorId = GetVendorIdIfVendor();
            var filterId = IsAdmin ? vendorId : currentVendorId;

            var results = await _analyticsService.GetCategorySalesAsync(
                startDate,
                endDate,
                filterId);

            return Ok(results);
        }

        [HttpGet("products/{productId}/timeseries")]
        [ProducesResponseType(typeof(List<ProductTimeSeriesDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProductTimeSeries(
            string productId,
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] TimeGranularity granularity = TimeGranularity.Monthly,
            [FromQuery] Guid? vendorId = null)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                return BadRequest(new { error = "productId is required" });
            }

            if (startDate > endDate)
            {
                return BadRequest(new { error = "startDate cannot be after endDate" });
            }

            if (endDate > DateTime.UtcNow.AddDays(1))
            {
                return BadRequest(new { error = "endDate cannot be in the future" });
            }

            var currentVendorId = GetVendorIdIfVendor();
            var filterId = IsAdmin ? vendorId : currentVendorId;

            var results = await _analyticsService.GetProductTimeSeriesAsync(
                productId,
                startDate,
                endDate,
                granularity,
                filterId);

            return Ok(results);
        }

        [HttpGet("revenue/trend")]
        [ProducesResponseType(typeof(List<RevenueTrendDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRevenueTrend(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] TimeGranularity granularity = TimeGranularity.Monthly,
            [FromQuery] Guid? vendorId = null)
        {
            if (startDate > endDate)
            {
                return BadRequest(new { error = "startDate cannot be after endDate" });
            }

            if (endDate > DateTime.UtcNow.AddDays(1))
            {
                return BadRequest(new { error = "endDate cannot be in the future" });
            }

            var currentVendorId = GetVendorIdIfVendor();
            var filterId = IsAdmin ? vendorId : currentVendorId;

            var results = await _analyticsService.GetRevenueTrendAsync(
                startDate,
                endDate,
                granularity,
                filterId);

            return Ok(results);
        }
    }
}
