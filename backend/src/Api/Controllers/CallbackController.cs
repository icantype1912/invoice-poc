using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace invoice_v1.src.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallbackController : ControllerBase
    {
        private readonly IJobService _jobService;
        private readonly IInvoiceService _invoiceService;
        private readonly IHmacValidator _hmacValidator;
        private readonly ILogger<CallbackController> _logger;

        public CallbackController(
            IJobService jobService,
            IInvoiceService invoiceService,
            IHmacValidator hmacValidator,
            ILogger<CallbackController> logger)
        {
            _jobService = jobService;
            _invoiceService = invoiceService;
            _hmacValidator = hmacValidator;
            _logger = logger;
        }

        [HttpPost]
        [Consumes("application/json")]
        public async Task<IActionResult> HandleCallback()
        {
            try
            {
                // 1. Enable buffering so we can read the stream multiple times if needed
                Request.EnableBuffering();

                // 2. Read RAW bytes for HMAC validation
                using var memoryStream = new MemoryStream();
                await Request.Body.CopyToAsync(memoryStream);
                var requestBytes = memoryStream.ToArray();
                var requestBody = Encoding.UTF8.GetString(requestBytes);

                // 3. Reset stream position for safety (though we have the string now)
                Request.Body.Position = 0;

                // 4. Validate HMAC
                if (!Request.Headers.TryGetValue("X-Callback-HMAC", out var hmacHeader))
                {
                    return Unauthorized(new { error = "Missing X-Callback-HMAC header" });
                }

                if (!_hmacValidator.ValidateHmac(requestBody, hmacHeader!))
                {
                    _logger.LogWarning("HMAC validation failed for request.");
                    return Unauthorized(new { error = "Invalid HMAC signature" });
                }

                // 5. Deserialize Request
                CallbackRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<CallbackRequest>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid JSON format in callback.");
                    return BadRequest(new { error = "Invalid JSON format" });
                }

                if (request == null)
                {
                    return BadRequest(new { error = "Invalid request payload" });
                }

                // 6. Idempotency Check
                var job = await _jobService.GetJobByIdAsync(request.JobId);
                if (job == null)
                {
                    return NotFound(new { error = $"Job {request.JobId} not found" });
                }

                if (job.Status == "COMPLETED" || job.Status == "INVALID" || job.Status == "FAILED")
                {
                    _logger.LogInformation("Callback received for job {JobId} which is already {Status}. Ignoring.", request.JobId, job.Status);
                    return Ok(new { success = true, jobId = request.JobId, message = "Job already processed (idempotent)" });
                }

                // 7. Process based on status
                switch (request.Status.ToUpperInvariant())
                {
                    case "COMPLETED":
                        if (request.Result == null) throw new ArgumentException("Result is required for COMPLETED status");

                        // Parse result safely
                        var resultJson = JsonSerializer.Serialize(request.Result);
                        var resultObj = JsonSerializer.Deserialize<JsonElement>(resultJson);
                        var resultDoc = JsonDocument.Parse(resultJson);

                        await _invoiceService.CreateOrUpdateInvoiceFromCallbackAsync(request.JobId, resultObj);
                        await _jobService.CompleteJobAsync(request.JobId, resultDoc);
                        _logger.LogInformation("Job {JobId} completed successfully.", request.JobId);
                        break;

                    case "INVALID":
                        var reasonJson = JsonSerializer.SerializeToDocument(new { message = request.Reason ?? "No reason provided" });
                        await _jobService.MarkInvalidAsync(request.JobId, reasonJson);
                        await _jobService.CreateInvalidInvoiceFromJobAsync(request.JobId, reasonJson);
                        break;

                    case "FAILED":
                        var errorJson = JsonSerializer.SerializeToDocument(new { message = request.Reason ?? "Worker reported failure" });
                        await _jobService.MarkFailedAsync(request.JobId, errorJson);
                        _logger.LogError("Job {JobId} marked as FAILED by worker.", request.JobId);
                        break;

                    default:
                        return BadRequest(new { error = $"Invalid status: {request.Status}" });
                }

                return Ok(new { success = true, jobId = request.JobId, status = request.Status });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing callback.");
                return StatusCode(500, new { error = "Internal server error processing callback" });
            }
        }
    }
}
