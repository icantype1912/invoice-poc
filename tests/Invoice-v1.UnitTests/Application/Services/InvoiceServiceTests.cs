using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Services;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Repositories;
using invoice_v1.tests.Helpers;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace invoice_v1.tests.Application.Services
{
    public class InvoiceServiceTests
    {
        private readonly Mock<IInvoiceRepository> _mockInvoiceRepo;
        private readonly Mock<IProductRepository> _mockProductRepo;
        private readonly Mock<IFileChangeLogRepository> _mockFileLogRepo;
        private readonly Mock<IJobRepository> _mockJobRepo;
        private readonly Mock<ILogger<InvoiceService>> _mockLogger;
        private readonly Mock<IDbContextFacade> _mockDb;
        private readonly Mock<IDbContextTransaction> _mockTx;
        private readonly InvoiceService _sut;

        public InvoiceServiceTests()
        {
            _mockInvoiceRepo = new Mock<IInvoiceRepository>();
            _mockProductRepo = new Mock<IProductRepository>();
            _mockFileLogRepo = new Mock<IFileChangeLogRepository>();
            _mockJobRepo = new Mock<IJobRepository>();
            _mockLogger = new Mock<ILogger<InvoiceService>>();
            _mockDb = new Mock<IDbContextFacade>();
            _mockTx = new Mock<IDbContextTransaction>();

            _mockTx.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _mockTx.Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _mockTx.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);

            _mockDb.Setup(d => d.CreateExecutionStrategy()).Returns(new NoRetryStrategy());
            _mockDb.Setup(d => d.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(_mockTx.Object);

            _sut = new InvoiceService(
                _mockInvoiceRepo.Object,
                _mockProductRepo.Object,
                _mockFileLogRepo.Object,
                _mockJobRepo.Object,
                _mockDb.Object,
                _mockLogger.Object);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────

        private static JobQueue BuildJob(string fileId = "file-123", string originalName = "invoice.pdf") => new()
        {
            Id = Guid.NewGuid(),
            JobType = "INVOICE_EXTRACTION",
            Status = "PENDING",
            PayloadJson = JsonSerializer.SerializeToDocument(new { fileId, originalName }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        private static object BuildCallbackResult(
            string invoiceNumber = "INV-001",
            decimal totalAmount = 100m,
            object[]? lineItems = null,
            string? currency = null,
            object? billTo = null,
            object? shipTo = null,
            object? discount = null,
            string? invoiceDate = null) => new
            {
                InvoiceNumber = invoiceNumber,
                TotalAmount = totalAmount,
                LineItems = lineItems ?? DefaultLineItems(),
                Currency = currency,
                BillTo = billTo,
                ShipTo = shipTo,
                Discount = discount,
                InvoiceDate = invoiceDate
            };

        private static object[] DefaultLineItems() =>
        [
            new { ProductId = "P001", ProductName = "Widget", Category = "Electronics,Gadgets", Quantity = 2m, UnitRate = 50m, Amount = 100m }
        ];

        private static Invoice BuildInvoice(string fileId = "file-123") => new()
        {
            Id = Guid.NewGuid(),
            DriveFileId = fileId,
            LineItems = new List<InvoiceLine>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        private static Product BuildProduct(string productId = "P001") => new()
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ProductName = "Widget",
            Category = "Electronics",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        private void SetupDefaultRepos(Invoice? existingInvoice = null, Product? existingProduct = null)
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
                        .ReturnsAsync(BuildJob());
            _mockFileLogRepo.Setup(r => r.GetLatestByFileIdAsync(It.IsAny<string>()))
                            .ReturnsAsync((FileChangeLog?)null);
            _mockInvoiceRepo.Setup(r => r.GetByFileIdAsync(It.IsAny<string>(), true))
                            .ReturnsAsync(existingInvoice);
            _mockInvoiceRepo.Setup(r => r.CreateAsync(It.IsAny<Invoice>()))
                            .ReturnsAsync((Invoice inv) => inv);
            _mockInvoiceRepo.Setup(r => r.DeleteLineItemsAsync(It.IsAny<IEnumerable<InvoiceLine>>()))
                            .Returns(Task.CompletedTask);
            _mockInvoiceRepo.Setup(r => r.SaveChangesAsync())
                            .ReturnsAsync(1);
            _mockProductRepo.Setup(r => r.GetByProductIdAsync(It.IsAny<string>()))
                            .ReturnsAsync(existingProduct);
            _mockProductRepo.Setup(r => r.CreateAsync(It.IsAny<Product>()))
                            .ReturnsAsync((Product p) => p);
        }

        // ────────────────────────────────────────────────────────────────────────────
        #region GetInvoiceByIdAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetInvoiceByIdAsync_NotFound_ReturnsNull()
        {
            _mockInvoiceRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), true)).ReturnsAsync((Invoice?)null);
            Assert.Null(await _sut.GetInvoiceByIdAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task GetInvoiceByIdAsync_Found_ReturnsMappedDto()
        {
            var invoice = BuildInvoice();
            invoice.InvoiceNumber = "INV-100";
            invoice.Currency = "EUR";
            _mockInvoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, true)).ReturnsAsync(invoice);

            var dto = await _sut.GetInvoiceByIdAsync(invoice.Id);

            Assert.NotNull(dto);
            Assert.Equal(invoice.Id, dto!.Id);
            Assert.Equal("INV-100", dto.InvoiceNumber);
            Assert.Equal("EUR", dto.Currency);
        }

        [Fact]
        public async Task GetInvoiceByIdAsync_Found_MapsLineItems()
        {
            var invoice = BuildInvoice();
            invoice.LineItems.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                ProductId = "P001",
                ProductName = "Widget",
                Quantity = 2,
                UnitRate = 50,
                Amount = 100
            });
            _mockInvoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, true)).ReturnsAsync(invoice);

            var dto = await _sut.GetInvoiceByIdAsync(invoice.Id);

            Assert.Single(dto!.LineItems);
            Assert.Equal("P001", dto.LineItems[0].ProductId);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetInvoiceByFileIdAsync
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetInvoiceByFileIdAsync_NotFound_ReturnsNull()
        {
            _mockInvoiceRepo.Setup(r => r.GetByFileIdAsync("missing", true)).ReturnsAsync((Invoice?)null);
            Assert.Null(await _sut.GetInvoiceByFileIdAsync("missing"));
        }

        [Fact]
        public async Task GetInvoiceByFileIdAsync_Found_ReturnsMappedDto()
        {
            var invoice = BuildInvoice("drive-abc");
            invoice.OriginalFileName = "receipt.pdf";
            _mockInvoiceRepo.Setup(r => r.GetByFileIdAsync("drive-abc", true)).ReturnsAsync(invoice);

            var dto = await _sut.GetInvoiceByFileIdAsync("drive-abc");

            Assert.NotNull(dto);
            Assert.Equal("drive-abc", dto!.DriveFileId);
            Assert.Equal("receipt.pdf", dto.OriginalFileName);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region GetInvoicesAsync — Pagination & Delegation
        // ────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task GetInvoicesAsync_PageLessThan1_NormalizesTo1(int badPage)
        {
            _mockInvoiceRepo.Setup(r => r.GetInvoiceCountAsync(null)).ReturnsAsync(0);
            _mockInvoiceRepo.Setup(r => r.GetInvoicesAsync(null, 0, 50)).ReturnsAsync(new List<Invoice>());

            await _sut.GetInvoicesAsync(null, badPage, 50);

            _mockInvoiceRepo.Verify(r => r.GetInvoicesAsync(null, 0, 50), Times.Once);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        public async Task GetInvoicesAsync_InvalidPageSize_NormalizesTo50(int badSize)
        {
            _mockInvoiceRepo.Setup(r => r.GetInvoiceCountAsync(null)).ReturnsAsync(0);
            _mockInvoiceRepo.Setup(r => r.GetInvoicesAsync(null, 0, 50)).ReturnsAsync(new List<Invoice>());

            await _sut.GetInvoicesAsync(null, 1, badSize);

            _mockInvoiceRepo.Verify(r => r.GetInvoicesAsync(null, 0, 50), Times.Once);
        }

        [Theory]
        [InlineData(1, 10, 0)]
        [InlineData(3, 10, 20)]
        [InlineData(2, 25, 25)]
        public async Task GetInvoicesAsync_CalculatesSkipCorrectly(int page, int pageSize, int expectedSkip)
        {
            _mockInvoiceRepo.Setup(r => r.GetInvoiceCountAsync(null)).ReturnsAsync(100);
            _mockInvoiceRepo.Setup(r => r.GetInvoicesAsync(null, expectedSkip, pageSize))
                            .ReturnsAsync(new List<Invoice>());

            await _sut.GetInvoicesAsync(null, page, pageSize);

            _mockInvoiceRepo.Verify(r => r.GetInvoicesAsync(null, expectedSkip, pageSize), Times.Once);
        }

        [Fact]
        public async Task GetInvoicesAsync_ReturnsTotalAndMappedDtos()
        {
            var invoice = BuildInvoice();
            invoice.InvoiceNumber = "INV-200";
            _mockInvoiceRepo.Setup(r => r.GetInvoiceCountAsync(null)).ReturnsAsync(1);
            _mockInvoiceRepo.Setup(r => r.GetInvoicesAsync(null, 0, 50))
                            .ReturnsAsync(new List<Invoice> { invoice });

            var (dtos, total) = await _sut.GetInvoicesAsync(null, 1, 50);

            Assert.Equal(1, total);
            Assert.Single(dtos);
            Assert.Equal("INV-200", dtos[0].InvoiceNumber);
        }

        [Fact]
        public async Task GetInvoicesAsync_PassesVendorIdToRepository()
        {
            var vendorId = Guid.NewGuid();
            _mockInvoiceRepo.Setup(r => r.GetInvoiceCountAsync(vendorId)).ReturnsAsync(0);
            _mockInvoiceRepo.Setup(r => r.GetInvoicesAsync(vendorId, 0, 50)).ReturnsAsync(new List<Invoice>());

            await _sut.GetInvoicesAsync(vendorId, 1, 50);

            _mockInvoiceRepo.Verify(r => r.GetInvoiceCountAsync(vendorId), Times.Once);
            _mockInvoiceRepo.Verify(r => r.GetInvoicesAsync(vendorId, 0, 50), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateOrUpdate — ValidateCriticalFields
        // ────────────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task CreateOrUpdate_BlankInvoiceNumber_Throws(string blank)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult(invoiceNumber: blank)));
            Assert.Contains("InvoiceNumber", ex.Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-50)]
        public async Task CreateOrUpdate_ZeroOrNegativeTotalAmount_Throws(decimal amount)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult(totalAmount: amount)));
            Assert.Contains("TotalAmount", ex.Message);
        }

        [Fact]
        public async Task CreateOrUpdate_MissingLineItemsProperty_Throws()
        {
            var result = new { InvoiceNumber = "INV-001", TotalAmount = 100m };
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), result));
            Assert.Contains("LineItems", ex.Message);
        }

        [Fact]
        public async Task CreateOrUpdate_EmptyLineItemsArray_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(),
                    BuildCallbackResult(lineItems: Array.Empty<object>())));
        }

        [Fact]
        public async Task CreateOrUpdate_MultipleValidationErrors_AllPresentInMessage()
        {
            var result = new { InvoiceNumber = "", TotalAmount = 0m, LineItems = Array.Empty<object>() };
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), result));
            Assert.Contains("InvoiceNumber", ex.Message);
            Assert.Contains("TotalAmount", ex.Message);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateOrUpdate — Job / Payload Guard Clauses
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateOrUpdate_JobNotFound_ThrowsWithJobId()
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((JobQueue?)null);
            var jobId = Guid.NewGuid();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CreateOrUpdateInvoiceFromCallbackAsync(jobId, BuildCallbackResult()));
            Assert.Contains(jobId.ToString(), ex.Message);
        }

        [Fact]
        public async Task CreateOrUpdate_JobPayloadNull_Throws()
        {
            var job = new JobQueue
            {
                Id = Guid.NewGuid(),
                JobType = "INVOICE_EXTRACTION",
                Status = "PENDING",
                PayloadJson = null!,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync(job);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult()));
        }

        [Fact]
        public async Task CreateOrUpdate_FileIdMissingFromPayload_Throws()
        {
            var job = new JobQueue
            {
                Id = Guid.NewGuid(),
                JobType = "INVOICE_EXTRACTION",
                Status = "PENDING",
                PayloadJson = JsonSerializer.SerializeToDocument(new { originalName = "test.pdf" }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync(job);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult()));
            Assert.Contains("FileId", ex.Message);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateOrUpdate — Create Path
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateOrUpdate_NewInvoice_CallsInvoiceCreateAsync()
        {
            SetupDefaultRepos();
            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());
            _mockInvoiceRepo.Verify(r => r.CreateAsync(It.IsAny<Invoice>()), Times.Once);
        }

        [Fact]
        public async Task CreateOrUpdate_NewInvoice_SetsFileIdAndFileName()
        {
            SetupDefaultRepos();
            Invoice? created = null;
            _mockInvoiceRepo.Setup(r => r.CreateAsync(It.IsAny<Invoice>()))
                            .ReturnsAsync((Invoice inv) => { created = inv; return inv; });

            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());

            Assert.Equal("file-123", created!.DriveFileId);
            Assert.Equal("invoice.pdf", created.OriginalFileName);
        }

        [Fact]
        public async Task CreateOrUpdate_NewInvoice_SetsUploadedByVendorId()
        {
            var vendorId = Guid.NewGuid();
            SetupDefaultRepos();
            _mockFileLogRepo.Setup(r => r.GetLatestByFileIdAsync("file-123"))
                            .ReturnsAsync(new FileChangeLog { UploadedByVendorId = vendorId });
            Invoice? created = null;
            _mockInvoiceRepo.Setup(r => r.CreateAsync(It.IsAny<Invoice>()))
                            .ReturnsAsync((Invoice inv) => { created = inv; return inv; });

            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());

            Assert.Equal(vendorId, created!.UploadedByVendorId);
        }

        [Fact]
        public async Task CreateOrUpdate_NewInvoice_CallsSaveChangesAsync()
        {
            SetupDefaultRepos();
            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());
            _mockInvoiceRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task CreateOrUpdate_NewInvoice_CommitsTransaction()
        {
            SetupDefaultRepos();
            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());
            _mockTx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateOrUpdate — Update Path
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateOrUpdate_ExistingInvoice_DoesNotCallCreateAsync()
        {
            SetupDefaultRepos(existingInvoice: BuildInvoice());
            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());
            _mockInvoiceRepo.Verify(r => r.CreateAsync(It.IsAny<Invoice>()), Times.Never);
        }

        [Fact]
        public async Task CreateOrUpdate_ExistingInvoice_CallsDeleteLineItems()
        {
            SetupDefaultRepos(existingInvoice: BuildInvoice());
            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());
            _mockInvoiceRepo.Verify(r => r.DeleteLineItemsAsync(It.IsAny<IEnumerable<InvoiceLine>>()), Times.Once);
        }

        [Fact]
        public async Task CreateOrUpdate_ExistingInvoice_UpdatesInvoiceNumber()
        {
            var existing = BuildInvoice();
            existing.InvoiceNumber = "OLD-001";
            SetupDefaultRepos(existingInvoice: existing);

            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(),
                BuildCallbackResult(invoiceNumber: "NEW-999"));

            Assert.Equal("NEW-999", dto.InvoiceNumber);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateOrUpdate — Field Mapping
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateOrUpdate_CurrencyAbsent_DefaultsToUSD()
        {
            SetupDefaultRepos();
            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult(currency: null));
            Assert.Equal("USD", dto.Currency);
        }

        [Fact]
        public async Task CreateOrUpdate_CurrencyPresent_UsesProvidedValue()
        {
            SetupDefaultRepos();
            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult(currency: "GBP"));
            Assert.Equal("GBP", dto.Currency);
        }

        [Fact]
        public async Task CreateOrUpdate_BillToPresent_SetsBillToName()
        {
            SetupDefaultRepos();
            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(),
                BuildCallbackResult(billTo: new { Name = "Acme Corp" }));
            Assert.Equal("Acme Corp", dto.BillToName);
        }

        [Fact]
        public async Task CreateOrUpdate_BillToAbsent_NullsBillToName()
        {
            SetupDefaultRepos();
            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());
            Assert.Null(dto.BillToName);
        }

        [Fact]
        public async Task CreateOrUpdate_ShipToPresent_SetsAllFields()
        {
            SetupDefaultRepos();
            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(),
                BuildCallbackResult(shipTo: new { City = "Mumbai", State = "MH", Country = "IN" }));

            Assert.Equal("Mumbai", dto.ShipTo?.City);
            Assert.Equal("MH", dto.ShipTo?.State);
            Assert.Equal("IN", dto.ShipTo?.Country);
        }

        [Fact]
        public async Task CreateOrUpdate_ShipToAbsent_NullsAllShipToFields()
        {
            SetupDefaultRepos();
            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());
            Assert.Null(dto.ShipTo?.City);
            Assert.Null(dto.ShipTo?.State);
            Assert.Null(dto.ShipTo?.Country);
        }

        [Fact]
        public async Task CreateOrUpdate_DiscountPresent_SetsDiscountFields()
        {
            SetupDefaultRepos();
            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(),
                BuildCallbackResult(discount: new { Percentage = 10m, Amount = 5m }));

            Assert.Equal(10m, dto.Discount?.Percentage);
            Assert.Equal(5m, dto.Discount?.Amount);
        }

        [Fact]
        public async Task CreateOrUpdate_DiscountAbsent_NullsDiscountFields()
        {
            SetupDefaultRepos();
            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());
            Assert.Null(dto.Discount?.Percentage);
            Assert.Null(dto.Discount?.Amount);
        }

        [Fact]
        public async Task CreateOrUpdate_InvoiceDatePresent_ParsedAsUtc()
        {
            SetupDefaultRepos();
            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(),
                BuildCallbackResult(invoiceDate: "2024-06-15T10:00:00Z"));

            Assert.NotNull(dto.InvoiceDate);
            Assert.Equal(DateTimeKind.Utc, dto.InvoiceDate!.Value.Kind);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateOrUpdate — Line Item Processing
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateOrUpdate_LineItemMissingProductId_IsSkipped()
        {
            SetupDefaultRepos();
            var result = BuildCallbackResult(lineItems:
            [
                new { ProductId = (string?)null, ProductName = "X",      Quantity = 1m, UnitRate = 10m,  Amount = 10m  },
                new { ProductId = "P001",         ProductName = "Widget", Quantity = 1m, UnitRate = 100m, Amount = 100m }
            ]);

            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), result);
            Assert.Single(dto.LineItems);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task CreateOrUpdate_LineItemInvalidQuantity_IsSkipped(decimal qty)
        {
            SetupDefaultRepos();
            _mockProductRepo.Setup(r => r.GetByProductIdAsync("P002")).ReturnsAsync((Product?)null);

            var result = BuildCallbackResult(lineItems:
            [
                new { ProductId = "P001", ProductName = "Widget", Quantity = qty, UnitRate = 10m, Amount = 0m  },
                new { ProductId = "P002", ProductName = "Gadget", Quantity = 1m,  UnitRate = 50m, Amount = 50m }
            ]);

            var dto = await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), result);
            Assert.Single(dto.LineItems);
            Assert.Equal("P002", dto.LineItems[0].ProductId);
        }

        [Fact]
        public async Task CreateOrUpdate_AllLineItemsInvalid_ThrowsNoValidLineItems()
        {
            SetupDefaultRepos();
            var result = BuildCallbackResult(lineItems:
            [
                new { ProductId = "P001", ProductName = "Widget", Quantity = 0m, UnitRate = 10m, Amount = 0m }
            ]);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), result));
            Assert.Contains("No valid line items", ex.Message);
        }

        [Fact]
        public async Task CreateOrUpdate_NewProduct_CallsProductCreateAsync()
        {
            SetupDefaultRepos(existingProduct: null);
            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());
            _mockProductRepo.Verify(r => r.CreateAsync(It.IsAny<Product>()), Times.Once);
        }

        [Fact]
        public async Task CreateOrUpdate_ExistingProduct_DoesNotCallProductCreateAsync()
        {
            SetupDefaultRepos(existingProduct: BuildProduct());
            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult());
            _mockProductRepo.Verify(r => r.CreateAsync(It.IsAny<Product>()), Times.Never);
        }

        [Fact]
        public async Task CreateOrUpdate_NewProduct_ParsesCommaSeparatedCategory()
        {
            SetupDefaultRepos(existingProduct: null);
            Product? created = null;
            _mockProductRepo.Setup(r => r.CreateAsync(It.IsAny<Product>()))
                            .ReturnsAsync((Product p) => { created = p; return p; });

            var result = BuildCallbackResult(lineItems:
            [
                new { ProductId = "P001", ProductName = "Widget", Category = "Electronics,Gadgets", Quantity = 1m, UnitRate = 100m, Amount = 100m }
            ]);

            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), result);

            Assert.Equal("Electronics", created!.PrimaryCategory);
            Assert.Equal("Gadgets", created.SecondaryCategory);
        }

        [Fact]
        public async Task CreateOrUpdate_NewProduct_SingleCategory_SetsOnlyPrimary()
        {
            SetupDefaultRepos(existingProduct: null);
            Product? created = null;
            _mockProductRepo.Setup(r => r.CreateAsync(It.IsAny<Product>()))
                            .ReturnsAsync((Product p) => { created = p; return p; });

            var result = BuildCallbackResult(lineItems:
            [
                new { ProductId = "P001", ProductName = "Widget", Category = "Electronics", Quantity = 1m, UnitRate = 100m, Amount = 100m }
            ]);

            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), result);

            Assert.Equal("Electronics", created!.PrimaryCategory);
            Assert.Null(created.SecondaryCategory);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateOrUpdate — Product Stats Aggregation
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateOrUpdate_SameProductTwoLines_IncrementsInvoiceCountOnce()
        {
            var product = BuildProduct("P001");
            product.InvoiceCount = 0;
            SetupDefaultRepos(existingProduct: product);

            var result = BuildCallbackResult(lineItems:
            [
                new { ProductId = "P001", ProductName = "Widget", Quantity = 1m, UnitRate = 50m, Amount = 50m  },
                new { ProductId = "P001", ProductName = "Widget", Quantity = 2m, UnitRate = 50m, Amount = 100m }
            ]);

            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), result);
            Assert.Equal(1, product.InvoiceCount);
        }

        [Fact]
        public async Task CreateOrUpdate_MultipleLines_AccumulatesQuantityAndRevenue()
        {
            var product = BuildProduct("P001");
            product.TotalQuantitySold = 0;
            product.TotalRevenue = 0;
            SetupDefaultRepos(existingProduct: product);

            var result = BuildCallbackResult(lineItems:
            [
                new { ProductId = "P001", ProductName = "Widget", Quantity = 3m, UnitRate = 50m, Amount = 150m },
                new { ProductId = "P001", ProductName = "Widget", Quantity = 2m, UnitRate = 50m, Amount = 100m }
            ]);

            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), result);
            Assert.Equal(5m, product.TotalQuantitySold);
            Assert.Equal(250m, product.TotalRevenue);
        }

        [Fact]
        public async Task CreateOrUpdate_InvoiceDateNewer_UpdatesProductLastSoldDate()
        {
            var product = BuildProduct("P001");
            product.LastSoldDate = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            SetupDefaultRepos(existingProduct: product);

            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(),
                BuildCallbackResult(invoiceDate: "2024-06-01T00:00:00Z"));

            Assert.Equal(new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), product.LastSoldDate);
        }

        [Fact]
        public async Task CreateOrUpdate_InvoiceDateOlder_DoesNotUpdateLastSoldDate()
        {
            var product = BuildProduct("P001");
            product.LastSoldDate = new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc);
            SetupDefaultRepos(existingProduct: product);

            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(),
                BuildCallbackResult(invoiceDate: "2023-01-01T00:00:00Z"));

            Assert.Equal(new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc), product.LastSoldDate);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateOrUpdate — Existing Product Update
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateOrUpdate_ExistingProductNameChanged_UpdatesNameAndCategory()
        {
            var product = BuildProduct("P001");
            product.ProductName = "Old Name";
            product.Category = "Old Category";
            SetupDefaultRepos(existingProduct: product);

            var result = BuildCallbackResult(lineItems:
            [
                new { ProductId = "P001", ProductName = "New Name", Category = "New Cat,Sub", Quantity = 1m, UnitRate = 100m, Amount = 100m }
            ]);

            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), result);

            Assert.Equal("New Name", product.ProductName);
            Assert.Equal("New Cat", product.PrimaryCategory);
            Assert.Equal("Sub", product.SecondaryCategory);
        }

        [Fact]
        public async Task CreateOrUpdate_ExistingProductNullCategory_NullsPrimaryAndSecondary()
        {
            var product = BuildProduct("P001");
            product.PrimaryCategory = "Electronics";
            product.SecondaryCategory = "Gadgets";
            SetupDefaultRepos(existingProduct: product);

            var result = BuildCallbackResult(lineItems:
            [
                new { ProductId = "P001", ProductName = "Widget", Category = (string?)null, Quantity = 1m, UnitRate = 100m, Amount = 100m }
            ]);

            await _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), result);

            Assert.Null(product.PrimaryCategory);
            Assert.Null(product.SecondaryCategory);
        }

        #endregion

        // ────────────────────────────────────────────────────────────────────────────
        #region CreateOrUpdate — Transaction Behaviour
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateOrUpdate_OnException_RollsBackTransaction()
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((JobQueue?)null);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult()));

            _mockTx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateOrUpdate_OnException_DoesNotCommit()
        {
            _mockJobRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((JobQueue?)null);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.CreateOrUpdateInvoiceFromCallbackAsync(Guid.NewGuid(), BuildCallbackResult()));

            _mockTx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        #endregion
    }
}
