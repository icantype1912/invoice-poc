using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace invoice_v1.src.Infrastructure.Repositories
{
    public class AnalyticsRepository : IAnalyticsRepository
    {
        private readonly ApplicationDbContext _context;

        public AnalyticsRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<ProductSalesDto>> GetProductSalesByDateRangeAsync(
            DateTime startDate,
            DateTime endDate,
            string? category,
            Guid? vendorId)
        {
            var query = _context.InvoiceLines
                .Include(il => il.Invoice)
                .Where(il => (il.Invoice.InvoiceDate ?? il.Invoice.CreatedAt) >= startDate &&
                             (il.Invoice.InvoiceDate ?? il.Invoice.CreatedAt) <= endDate);

            if (vendorId.HasValue)
                query = query.Where(il => il.Invoice.UploadedByVendorId == vendorId.Value);

            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(il => il.Category == category);

            var grouped = await query
                .GroupBy(il => new { il.ProductId, il.ProductName, il.Category })
                .Select(g => new
                {
                    Key = g.Key,
                    TotalQuantity = g.Sum(il => il.Quantity),
                    TotalRevenue = g.Sum(il => il.Amount),
                    InvoiceCount = g.Select(il => il.InvoiceId).Distinct().Count(),
                    AverageUnitRate = g.Average(il => il.UnitRate)
                })
                .ToListAsync();

            return grouped.Select(g => new ProductSalesDto
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.ProductName,
                Category = g.Key.Category,
                TotalQuantity = g.TotalQuantity,
                TotalRevenue = g.TotalRevenue,
                InvoiceCount = g.InvoiceCount,
                AverageUnitRate = g.AverageUnitRate
            }).ToList();
        }

        public async Task<List<ProductTrendDto>> GetTrendingProductsAsync(
            DateTime startDate,
            DateTime endDate,
            int topN,
            Guid? vendorId)
        {
            var query = _context.InvoiceLines
                .Include(il => il.Invoice)
                .Where(il => (il.Invoice.InvoiceDate ?? il.Invoice.CreatedAt) >= startDate &&
                             (il.Invoice.InvoiceDate ?? il.Invoice.CreatedAt) <= endDate);

            if (vendorId.HasValue)
                query = query.Where(il => il.Invoice.UploadedByVendorId == vendorId.Value);

            var grouped = await query
                .GroupBy(il => new { il.ProductId, il.ProductName, il.Category })
                .Select(g => new
                {
                    Key = g.Key,
                    TotalQuantity = g.Sum(il => il.Quantity),
                    TotalRevenue = g.Sum(il => il.Amount),
                    InvoiceCount = g.Select(il => il.InvoiceId).Distinct().Count()
                })
                .OrderByDescending(x => x.TotalRevenue)
                .Take(topN)
                .ToListAsync();

            int rank = 1;
            return grouped.Select(g => new ProductTrendDto
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.ProductName,
                Category = g.Key.Category,
                TotalQuantity = g.TotalQuantity,
                TotalRevenue = g.TotalRevenue,
                InvoiceCount = g.InvoiceCount,
                Rank = rank++,
                GrowthRate = 0
            }).ToList();
        }

        public async Task<List<CategorySalesDto>> GetCategorySalesAsync(
            DateTime startDate,
            DateTime endDate,
            Guid? vendorId)
        {
            var query = _context.InvoiceLines
                .Include(il => il.Invoice)
                .Where(il => (il.Invoice.InvoiceDate ?? il.Invoice.CreatedAt) >= startDate &&
                             (il.Invoice.InvoiceDate ?? il.Invoice.CreatedAt) <= endDate);

            if (vendorId.HasValue)
                query = query.Where(il => il.Invoice.UploadedByVendorId == vendorId.Value);

            var grouped = await query
                .GroupBy(il => il.Category ?? "Uncategorized")
                .Select(g => new
                {
                    Category = g.Key,
                    ProductCount = g.Select(il => il.ProductId).Distinct().Count(),
                    TotalQuantity = g.Sum(il => il.Quantity),
                    TotalRevenue = g.Sum(il => il.Amount),
                    InvoiceCount = g.Select(il => il.InvoiceId).Distinct().Count()
                })
                .ToListAsync();

            return grouped.Select(g => new CategorySalesDto
            {
                Category = g.Category,
                ProductCount = g.ProductCount,
                TotalQuantity = g.TotalQuantity,
                TotalRevenue = g.TotalRevenue,
                InvoiceCount = g.InvoiceCount,
                AverageOrderValue = g.InvoiceCount > 0 ? g.TotalRevenue / g.InvoiceCount : 0
            }).ToList();
        }

        // FIXED: Guid InvoiceId added to tuple — needed for Distinct().Count() in AggregateTimeSeries
        public async Task<List<(DateTime InvoiceDate, Guid InvoiceId, string ProductId, string ProductName, decimal Quantity, decimal Amount)>>
            GetProductTimeSeriesDataAsync(
                string productId,
                DateTime startDate,
                DateTime endDate,
                Guid? vendorId)
        {
            var query = _context.InvoiceLines
                .Include(il => il.Invoice)
                .Where(il => il.ProductId == productId &&
                             (il.Invoice.InvoiceDate ?? il.Invoice.CreatedAt) >= startDate &&
                             (il.Invoice.InvoiceDate ?? il.Invoice.CreatedAt) <= endDate);

            if (vendorId.HasValue)
                query = query.Where(il => il.Invoice.UploadedByVendorId == vendorId.Value);

            return await query
                .Select(il => new ValueTuple<DateTime, Guid, string, string, decimal, decimal>(
                    il.Invoice.InvoiceDate ?? il.Invoice.CreatedAt,
                    il.InvoiceId,       // ← ADDED
                    il.ProductId,
                    il.ProductName,
                    il.Quantity,
                    il.Amount))
                .ToListAsync();
        }
    }
}