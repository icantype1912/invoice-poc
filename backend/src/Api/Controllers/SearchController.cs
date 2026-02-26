using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace invoice_v1.src.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin,Vendor")]
    public class SearchController : ControllerBase
    {
        private readonly ISearchService _searchService;
        private readonly ILogger<SearchController> _logger;

        public SearchController(
            ISearchService searchService,
            ILogger<SearchController> logger)
        {
            _searchService = searchService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Search([FromBody] SearchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return BadRequest(new { error = "Query is required." });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")
                ?? "unknown";

            // Vendors are scoped to their own data automatically
            Guid? vendorId = null;
            var isVendor = User.IsInRole("Vendor");
            if (isVendor)
            {
                if (!Guid.TryParse(userId, out var vid))
                {
                    _logger.LogWarning(
                        "Vendor search attempted with invalid userId: {UserId}", userId);
                    return Unauthorized();
                }
                vendorId = vid;
            }

            var result = await _searchService.SearchAsync(
                request.Query, vendorId, userId);

            // 429 if rate limited
            if (result.Error?.Contains("Too many") == true)
                return StatusCode(429, result);

            return Ok(result);
        }
    }
}