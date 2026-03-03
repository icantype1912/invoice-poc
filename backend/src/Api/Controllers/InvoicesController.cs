using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace invoice_v1.src.Api.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    [ApiController]
    [Route("api/invoices")]
    public class InvoicesController : BaseAuthenticatedController
    {
        private readonly IInvoiceService _invoiceService;
        private readonly ILogger<InvoicesController> _logger;

        public InvoicesController(
            IInvoiceService invoiceService,
            ILogger<InvoicesController> logger)
        {
            _invoiceService = invoiceService;
            _logger = logger;
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetInvoiceById(Guid id)
        {
            var invoice = await _invoiceService.GetInvoiceByIdAsync(id);
            if (invoice == null)
            {
                return NotFound(new { error = $"Invoice {id} not found" });
            }

            var vendorId = GetVendorIdIfVendor();

            if (vendorId.HasValue && invoice.UploadedByVendorId != vendorId.Value)
            {
                _logger.LogWarning(
                    "Vendor {VendorId} attempted to access invoice {InvoiceId} owned by {OwnerId}",
                    vendorId,
                    id,
                    invoice.UploadedByVendorId);

                return Forbid();
            }

            return Ok(invoice);
        }

        [HttpGet("by-file/{fileId}")]
        [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetInvoiceByFileId(string fileId)
        {
            var invoice = await _invoiceService.GetInvoiceByFileIdAsync(fileId);
            if (invoice == null)
            {
                return NotFound(new { error = $"No invoice found for file {fileId}" });
            }

            var vendorId = GetVendorIdIfVendor();

            if (vendorId.HasValue && invoice.UploadedByVendorId != vendorId.Value)
            {
                return Forbid();
            }

            return Ok(invoice);
        }

        [HttpGet]
        [ProducesResponseType(typeof(InvoiceListResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetInvoices(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] Guid? vendorId = null)
        {
            var currentVendorId = GetVendorIdIfVendor();
            var filterId = IsAdmin ? vendorId : currentVendorId;

            var (invoices, total) = await _invoiceService.GetInvoicesAsync(filterId, page, pageSize);

            var response = new InvoiceListResponse
            {
                Invoices = invoices,
                Page = page,
                PageSize = pageSize,
                Total = total,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize)
            };

            return Ok(response);
        }
    }
}