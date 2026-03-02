using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Data;
using invoice_v1.src.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace invoice_v1.src.Application.Services
{
    public class InvoiceService : IInvoiceService
    {
        private readonly IInvoiceRepository _invoiceRepository;
        private readonly IProductRepository _productRepository;
        private readonly IFileChangeLogRepository _fileChangeLogRepository;
        private readonly IJobRepository _jobRepository;
        private readonly IDbContextFacade _db;
        private readonly ILogger<InvoiceService> _logger;

        public InvoiceService(
            IInvoiceRepository invoiceRepository,
            IProductRepository productRepository,
            IFileChangeLogRepository fileChangeLogRepository,
            IJobRepository jobRepository,
            IDbContextFacade db,
            ILogger<InvoiceService> logger)
        {
            _invoiceRepository = invoiceRepository;
            _productRepository = productRepository;
            _fileChangeLogRepository = fileChangeLogRepository;
            _jobRepository = jobRepository;
            _db = db;
            _logger = logger;
        }

        public async Task<InvoiceDto> CreateOrUpdateInvoiceFromCallbackAsync(Guid jobId, object result)
        {
            // FIX: Use execution strategy for retry-on-failure
            var strategy = _db.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.BeginTransactionAsync();

                try
                {
                    var resultJson = JsonSerializer.Serialize(result);
                    var extractedData = JsonSerializer.Deserialize<JsonElement>(resultJson);

                    ValidateCriticalFields(extractedData);

                    var job = await _jobRepository.GetByIdAsync(jobId);
                    if (job == null)
                    {
                        throw new InvalidOperationException($"Job {jobId} not found");
                    }

                    if (job.PayloadJson == null)
                    {
                        throw new InvalidOperationException("Job payload is missing");
                    }

                    var payload = job.PayloadJson.RootElement;

                    if (!payload.TryGetProperty("fileId", out var fileIdProp)
                        || fileIdProp.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidOperationException("FileId not found in job payload");
                    }

                    var fileId = fileIdProp.GetString();
                    if (string.IsNullOrWhiteSpace(fileId))
                    {
                        throw new InvalidOperationException("FileId is empty in job payload");
                    }

                    var fileName = GetStringProperty(payload, "originalName");

                    var fileChangeLog = await _fileChangeLogRepository.GetLatestByFileIdAsync(fileId);
                    var uploadedByVendorId = fileChangeLog?.UploadedByVendorId;

                    var existingInvoice = await _invoiceRepository.GetByFileIdAsync(fileId, includeLineItems: true);

                    Invoice invoice;
                    if (existingInvoice != null)
                    {
                        invoice = existingInvoice;
                        invoice.UpdatedAt = DateTime.UtcNow;

                        _logger.LogInformation(
                            "Updating existing invoice {InvoiceId} for vendor {VendorId}",
                            invoice.Id,
                            uploadedByVendorId);

                        await _invoiceRepository.DeleteLineItemsAsync(existingInvoice.LineItems);
                        invoice.LineItems.Clear();
                    }
                    else
                    {
                        invoice = new Invoice
                        {
                            Id = Guid.NewGuid(),
                            DriveFileId = fileId,
                            OriginalFileName = fileName,
                            UploadedByVendorId = uploadedByVendorId,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        await _invoiceRepository.CreateAsync(invoice);

                        _logger.LogInformation(
                            "Creating new invoice {InvoiceId} for vendor {VendorId}",
                            invoice.Id,
                            uploadedByVendorId);
                    }

                    invoice.InvoiceNumber = GetStringProperty(extractedData, "InvoiceNumber");
                    invoice.OrderId = GetStringProperty(extractedData, "OrderId");
                    invoice.VendorName = GetStringProperty(extractedData, "VendorName");
                    invoice.ShipMode = GetStringProperty(extractedData, "ShipMode");
                    invoice.Currency = GetStringProperty(extractedData, "Currency") ?? "USD";
                    invoice.Notes = GetStringProperty(extractedData, "Notes");
                    invoice.Terms = GetStringProperty(extractedData, "Terms");
                    invoice.InvoiceDate = GetDateTimeProperty(extractedData, "InvoiceDate");

                    if (extractedData.TryGetProperty("BillTo", out var billToElement)
                        && billToElement.ValueKind == JsonValueKind.Object)
                    {
                        invoice.BillToName = GetStringProperty(billToElement, "Name");
                    }
                    else
                    {
                        invoice.BillToName = null;
                    }

                    if (extractedData.TryGetProperty("ShipTo", out var shipToElement)
                        && shipToElement.ValueKind == JsonValueKind.Object)
                    {
                        invoice.ShipToCity = GetStringProperty(shipToElement, "City");
                        invoice.ShipToState = GetStringProperty(shipToElement, "State");
                        invoice.ShipToCountry = GetStringProperty(shipToElement, "Country");
                    }
                    else
                    {
                        invoice.ShipToCity = null;
                        invoice.ShipToState = null;
                        invoice.ShipToCountry = null;
                    }

                    invoice.Subtotal = GetDecimalProperty(extractedData, "Subtotal");
                    invoice.ShippingCost = GetDecimalProperty(extractedData, "ShippingCost");
                    invoice.TotalAmount = GetDecimalProperty(extractedData, "TotalAmount");
                    invoice.BalanceDue = GetDecimalProperty(extractedData, "BalanceDue");

                    if (extractedData.TryGetProperty("Discount", out var discountElement)
                        && discountElement.ValueKind == JsonValueKind.Object)
                    {
                        invoice.DiscountPercentage = GetDecimalProperty(discountElement, "Percentage");
                        invoice.DiscountAmount = GetDecimalProperty(discountElement, "Amount");
                    }
                    else
                    {
                        invoice.DiscountPercentage = null;
                        invoice.DiscountAmount = null;
                    }

                    invoice.ExtractedDataJson = JsonDocument.Parse(resultJson);

                    if (!extractedData.TryGetProperty("LineItems", out var lineItemsElement)
                        || lineItemsElement.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidOperationException("LineItems array is required");
                    }

                    var lineItemsArray = lineItemsElement.EnumerateArray().ToList();
                    if (lineItemsArray.Count == 0)
                    {
                        throw new InvalidOperationException("Invoice must have at least one line item");
                    }

                    var allProductIds = lineItemsArray
                        .Select(li => GetStringProperty(li, "ProductId"))
                        .Where(pid => !string.IsNullOrWhiteSpace(pid))
                        .Distinct()
                        .ToList();

                    var existingProducts = new Dictionary<string, Product>();
                    foreach (var pid in allProductIds)
                    {
                        var product = await _productRepository.GetByProductIdAsync(pid!);
                        if (product != null)
                        {
                            existingProducts[pid!] = product;
                        }
                    }

                    var processedProducts = new HashSet<Guid>();

                    foreach (var lineElement in lineItemsArray)
                    {
                        var productId = GetStringProperty(lineElement, "ProductId");
                        if (string.IsNullOrWhiteSpace(productId))
                        {
                            _logger.LogWarning("Skipping line item with missing ProductId");
                            continue;
                        }

                        var productName = GetStringProperty(lineElement, "ProductName") ?? "Unknown";
                        var category = GetStringProperty(lineElement, "Category");
                        var quantity = GetDecimalProperty(lineElement, "Quantity") ?? 0;
                        var unitRate = GetDecimalProperty(lineElement, "UnitRate") ?? 0;
                        var amount = GetDecimalProperty(lineElement, "Amount") ?? 0;

                        if (quantity <= 0)
                        {
                            _logger.LogWarning("Skipping line item {ProductId} with invalid quantity: {Quantity}",
                                productId, quantity);
                            continue;
                        }

                        Product product;
                        if (existingProducts.TryGetValue(productId, out var cachedProduct))
                        {
                            product = cachedProduct;

                            if (product.ProductName != productName || product.Category != category)
                            {
                                product.ProductName = productName;
                                product.Category = category;

                                if (!string.IsNullOrWhiteSpace(category))
                                {
                                    var parts = category.Split(',', StringSplitOptions.TrimEntries);
                                    product.PrimaryCategory = parts.Length > 0 ? parts[0] : null;
                                    product.SecondaryCategory = parts.Length > 1 ? parts[1] : null;
                                }
                                else
                                {
                                    product.PrimaryCategory = null;
                                    product.SecondaryCategory = null;
                                }

                                product.UpdatedAt = DateTime.UtcNow;
                            }
                        }
                        else
                        {
                            product = await FindOrCreateProductAsync(
                                productId,
                                productName,
                                category,
                                unitRate);

                            existingProducts[productId] = product;
                        }

                        var line = new InvoiceLine
                        {
                            Id = Guid.NewGuid(),
                            InvoiceId = invoice.Id,
                            ProductGuid = product.Id,
                            ProductId = productId,
                            ProductName = productName,
                            Category = category,
                            Quantity = quantity,
                            UnitRate = unitRate,
                            Amount = amount,
                            CreatedAt = DateTime.UtcNow
                        };

                        invoice.LineItems.Add(line);

                        product.TotalQuantitySold += quantity;
                        product.TotalRevenue += amount;

                        if (!processedProducts.Contains(product.Id))
                        {
                            product.InvoiceCount++;
                            processedProducts.Add(product.Id);
                        }

                        if (invoice.InvoiceDate.HasValue)
                        {
                            if (!product.LastSoldDate.HasValue
                                || invoice.InvoiceDate.Value > product.LastSoldDate.Value)
                            {
                                product.LastSoldDate = invoice.InvoiceDate.Value;
                            }
                        }

                        product.UpdatedAt = DateTime.UtcNow;
                    }

                    if (invoice.LineItems.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "No valid line items found. All line items were skipped due to missing or invalid data.");
                    }

                    await _invoiceRepository.SaveChangesAsync();

                    await transaction.CommitAsync();

                    _logger.LogInformation(
                        "✅ Invoice {InvoiceId} ({InvoiceNumber}) saved successfully with {LineCount} line items for vendor {VendorId}",
                        invoice.Id,
                        invoice.InvoiceNumber ?? "N/A",
                        invoice.LineItems.Count,
                        uploadedByVendorId);

                    return await MapToDtoAsync(invoice);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(
                        ex,
                        "Error creating/updating invoice for job {JobId}, transaction rolled back",
                        jobId);
                    throw;
                }
            });
        }

        private async Task<Product> FindOrCreateProductAsync(
            string productId,
            string productName,
            string? category,
            decimal? unitRate)
        {
            var product = await _productRepository.GetByProductIdAsync(productId);

            if (product == null)
            {
                string? primaryCategory = null;
                string? secondaryCategory = null;

                if (!string.IsNullOrWhiteSpace(category))
                {
                    var parts = category.Split(',', StringSplitOptions.TrimEntries);
                    if (parts.Length > 0) primaryCategory = parts[0];
                    if (parts.Length > 1) secondaryCategory = parts[1];
                }

                product = new Product
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    ProductName = productName,
                    Category = category,
                    PrimaryCategory = primaryCategory,
                    SecondaryCategory = secondaryCategory,
                    DefaultUnitRate = unitRate,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _productRepository.CreateAsync(product);
                // removed await _productRepository.SaveChangesAsync();

                _logger.LogInformation(
                    "Created new product {ProductId}: {ProductName} ({Category})",
                    productId,
                    productName,
                    category ?? "N/A");
            }
            else
            {
                if (product.ProductName != productName || product.Category != category)
                {
                    product.ProductName = productName;
                    product.Category = category;

                    if (!string.IsNullOrWhiteSpace(category))
                    {
                        var parts = category.Split(',', StringSplitOptions.TrimEntries);
                        product.PrimaryCategory = parts.Length > 0 ? parts[0] : null;
                        product.SecondaryCategory = parts.Length > 1 ? parts[1] : null;
                    }
                    else
                    {
                        product.PrimaryCategory = null;
                        product.SecondaryCategory = null;
                    }

                    product.UpdatedAt = DateTime.UtcNow;
                }
            }

            return product;
        }


        public async Task<InvoiceDto?> GetInvoiceByIdAsync(Guid id)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(id, includeLineItems: true);
            return invoice != null ? await MapToDtoAsync(invoice) : null;
        }

        public async Task<InvoiceDto?> GetInvoiceByFileIdAsync(string fileId)
        {
            var invoice = await _invoiceRepository.GetByFileIdAsync(fileId, includeLineItems: true);
            return invoice != null ? await MapToDtoAsync(invoice) : null;
        }

        public async Task<(List<InvoiceDto> Invoices, int Total)> GetInvoicesAsync(
            Guid? vendorId,
            int page,
            int pageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 50;

            var skip = (page - 1) * pageSize;

            var total = await _invoiceRepository.GetInvoiceCountAsync(vendorId);
            var invoices = await _invoiceRepository.GetInvoicesAsync(vendorId, skip, pageSize);

            var dtos = new List<InvoiceDto>();
            foreach (var invoice in invoices)
            {
                dtos.Add(await MapToDtoAsync(invoice));
            }

            _logger.LogInformation(
                "Retrieved {Count} invoices (page {Page} of {TotalPages}) for vendor {VendorId}",
                dtos.Count,
                page,
                (int)Math.Ceiling(total / (double)pageSize),
                vendorId?.ToString() ?? "ALL");

            return (dtos, total);
        }

        private void ValidateCriticalFields(JsonElement extractedData)
        {
            var errors = new List<string>();

            var invoiceNumber = GetStringProperty(extractedData, "InvoiceNumber");
            if (string.IsNullOrWhiteSpace(invoiceNumber))
            {
                errors.Add("InvoiceNumber is required");
            }

            var totalAmount = GetDecimalProperty(extractedData, "TotalAmount");
            if (!totalAmount.HasValue || totalAmount.Value <= 0)
            {
                errors.Add("TotalAmount is required and must be greater than 0");
            }

            if (!extractedData.TryGetProperty("LineItems", out var lineItems)
                || lineItems.ValueKind != JsonValueKind.Array)
            {
                errors.Add("LineItems array is required");
            }
            else if (lineItems.GetArrayLength() == 0)
            {
                errors.Add("LineItems array must contain at least one item");
            }

            if (errors.Any())
            {
                var errorMessage = "Critical validation failed: " + string.Join("; ", errors);
                _logger.LogError(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }
        }

        private async Task<InvoiceDto> MapToDtoAsync(Invoice invoice)
        {
            object? extractedData = null;

            if (invoice.ExtractedDataJson != null)
            {
                extractedData = JsonSerializer.Deserialize<object>(
                    invoice.ExtractedDataJson.RootElement.GetRawText());
            }

            return new InvoiceDto
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceDate = invoice.InvoiceDate,
                OrderId = invoice.OrderId,
                VendorName = invoice.VendorName,
                BillToName = invoice.BillToName,
                ShipTo = new ShipToDto
                {
                    City = invoice.ShipToCity,
                    State = invoice.ShipToState,
                    Country = invoice.ShipToCountry
                },
                ShipMode = invoice.ShipMode,
                Subtotal = invoice.Subtotal,
                Discount = new DiscountDto
                {
                    Percentage = invoice.DiscountPercentage,
                    Amount = invoice.DiscountAmount
                },
                ShippingCost = invoice.ShippingCost,
                TotalAmount = invoice.TotalAmount,
                BalanceDue = invoice.BalanceDue,
                Currency = invoice.Currency,
                Notes = invoice.Notes,
                Terms = invoice.Terms,
                DriveFileId = invoice.DriveFileId,
                OriginalFileName = invoice.OriginalFileName,
                ExtractedData = extractedData,
                UploadedByVendorId = invoice.UploadedByVendorId,
                CreatedAt = invoice.CreatedAt,
                UpdatedAt = invoice.UpdatedAt,
                LineItems = invoice.LineItems.Select(l => new InvoiceLineDto
                {
                    Id = l.Id,
                    ProductName = l.ProductName,
                    Category = l.Category,
                    ProductId = l.ProductId,
                    Quantity = l.Quantity,
                    UnitRate = l.UnitRate,
                    Amount = l.Amount
                }).ToList()
            };
        }

        private string? GetStringProperty(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop))
                return null;

            if (prop.ValueKind == JsonValueKind.Null)
                return null;

            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString();

            _logger.LogDebug(
                "Property {PropertyName} has unexpected type {ValueKind}, expected String or Null",
                propertyName,
                prop.ValueKind);

            return null;
        }

        private decimal? GetDecimalProperty(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop))
                return null;

            if (prop.ValueKind == JsonValueKind.Null)
                return null;

            if (prop.ValueKind == JsonValueKind.Number)
            {
                try
                {
                    return prop.GetDecimal();
                }
                catch (FormatException)
                {
                    try
                    {
                        return (decimal)prop.GetDouble();
                    }
                    catch
                    {
                        _logger.LogWarning(
                            "Failed to parse {PropertyName} as decimal (value out of range)",
                            propertyName);
                        return null;
                    }
                }
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                var str = prop.GetString();
                if (!string.IsNullOrWhiteSpace(str) && decimal.TryParse(str, out var value))
                    return value;
            }

            _logger.LogDebug(
                "Property {PropertyName} has unexpected type {ValueKind}, expected Number or Null",
                propertyName,
                prop.ValueKind);

            return null;
        }

        private DateTime? GetDateTimeProperty(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop))
                return null;

            if (prop.ValueKind == JsonValueKind.Null)
                return null;

            if (prop.ValueKind == JsonValueKind.String)
            {
                var str = prop.GetString();
                if (!string.IsNullOrWhiteSpace(str) && DateTime.TryParse(str, out var value))
                {
                    if (value.Kind == DateTimeKind.Unspecified)
                    {
                        value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                    }
                    else if (value.Kind == DateTimeKind.Local)
                    {
                        value = value.ToUniversalTime();
                    }

                    return value;
                }

                _logger.LogWarning(
                    "Failed to parse {PropertyName} as DateTime: {Value}",
                    propertyName,
                    str);
            }

            return null;
        }
    }
}
