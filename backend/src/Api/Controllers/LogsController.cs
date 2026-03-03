using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace invoice_v1.src.Api.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    [ApiController]
    [Route("api/[controller]")]
    public class LogsController : BaseAuthenticatedController
    {
        private readonly IFileChangeLogService _fileChangeLogService;
        private readonly ILogger<LogsController> _logger;

        public LogsController(
            IFileChangeLogService fileChangeLogService,
            ILogger<LogsController> logger)
        {
            _fileChangeLogService = fileChangeLogService;
            _logger = logger;
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetLogs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? changeType = null,
            [FromQuery] Guid? vendorId = null)
        {
            var currentVendorId = GetVendorIdIfVendor();
            var filterId = IsAdmin ? vendorId : currentVendorId;

            var result = await _fileChangeLogService.GetLogsAsync(filterId, changeType, page, pageSize);

            return Ok(result);
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetLogById(int id)
        {
            var vendorId = GetVendorIdIfVendor();

            var log = await _fileChangeLogService.GetLogByIdAsync(id, vendorId);

            if (log == null)
            {
                return NotFound(new { error = $"Log {id} not found" });
            }

            return Ok(log);
        }

        [HttpGet("stats")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetLogStats()
        {
            var vendorId = GetVendorIdIfVendor();

            // CHANGED: Service call
            var stats = await _fileChangeLogService.GetLogStatsAsync(vendorId);

            return Ok(stats);
        }
    }
}
