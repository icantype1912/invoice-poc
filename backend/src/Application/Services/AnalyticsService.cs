using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Repositories;

namespace invoice_v1.src.Application.Services
{
    public class AnalyticsService : IAnalyticsService
    {
        private readonly IAnalyticsRepository _analyticsRepository;
        private readonly ILogger<AnalyticsService> _logger;

        public AnalyticsService(
            IAnalyticsRepository analyticsRepository,
            ILogger<AnalyticsService> logger)
        {
            _analyticsRepository = analyticsRepository;
            _logger = logger;
        }

        public async Task<List<ProductSalesDto>> GetProductSalesByDateRangeAsync(
            DateTime startDate,
            DateTime endDate,
            string? category = null,
            Guid? vendorId = null)
        {
            _logger.LogInformation(
                "Getting product sales from {StartDate} to {EndDate} [Category: {Category}, Vendor: {VendorId}]",
                startDate, endDate, category ?? "ALL", vendorId?.ToString() ?? "ALL");

            return await _analyticsRepository.GetProductSalesByDateRangeAsync(
                startDate, endDate, category, vendorId);
        }

        public async Task<List<ProductTrendDto>> GetTrendingProductsAsync(
            DateTime startDate,
            DateTime endDate,
            int topN = 10,
            Guid? vendorId = null)
        {
            _logger.LogInformation(
                "Getting top {TopN} trending products from {StartDate} to {EndDate} [Vendor: {VendorId}]",
                topN, startDate, endDate, vendorId?.ToString() ?? "ALL");

            return await _analyticsRepository.GetTrendingProductsAsync(
                startDate, endDate, topN, vendorId);
        }

        public async Task<List<CategorySalesDto>> GetCategorySalesAsync(
            DateTime startDate,
            DateTime endDate,
            Guid? vendorId = null)
        {
            _logger.LogInformation(
                "Getting category sales from {StartDate} to {EndDate} [Vendor: {VendorId}]",
                startDate, endDate, vendorId?.ToString() ?? "ALL");

            return await _analyticsRepository.GetCategorySalesAsync(
                startDate, endDate, vendorId);
        }

        public async Task<List<ProductTimeSeriesDto>> GetProductTimeSeriesAsync(
            string productId,
            DateTime startDate,
            DateTime endDate,
            TimeGranularity granularity = TimeGranularity.Monthly,
            Guid? vendorId = null)
        {
            _logger.LogInformation(
                "Getting time series for product {ProductId} from {StartDate} to {EndDate} [Granularity: {Granularity}, Vendor: {VendorId}]",
                productId, startDate, endDate, granularity, vendorId?.ToString() ?? "ALL");

            var rawData = await _analyticsRepository.GetProductTimeSeriesDataAsync(
                productId, startDate, endDate, vendorId);

            return AggregateTimeSeries(rawData, granularity);
        }

        // FIXED: tuple now carries InvoiceId → Distinct().Count() gives real invoice count, not line-item count
        private List<ProductTimeSeriesDto> AggregateTimeSeries(
            List<(DateTime InvoiceDate, Guid InvoiceId, string ProductId, string ProductName, decimal Quantity, decimal Amount)> rawData,
            TimeGranularity granularity)
        {
            return rawData
                .GroupBy(d => new
                {
                    Period = granularity switch
                    {
                        TimeGranularity.Daily => d.InvoiceDate.Date,
                        TimeGranularity.Weekly => d.InvoiceDate.AddDays(-(int)d.InvoiceDate.DayOfWeek).Date,
                        TimeGranularity.Monthly => new DateTime(d.InvoiceDate.Year, d.InvoiceDate.Month, 1),
                        TimeGranularity.Quarterly => new DateTime(d.InvoiceDate.Year, (d.InvoiceDate.Month - 1) / 3 * 3 + 1, 1),
                        TimeGranularity.Yearly => new DateTime(d.InvoiceDate.Year, 1, 1),
                        _ => d.InvoiceDate.Date
                    },
                    d.ProductId,
                    d.ProductName
                })
                .Select(g => new ProductTimeSeriesDto
                {
                    Period = g.Key.Period,
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.ProductName,
                    Quantity = g.Sum(x => x.Quantity),
                    Revenue = g.Sum(x => x.Amount),
                    InvoiceCount = g.Select(x => x.InvoiceId).Distinct().Count()  // ← FIXED: was g.Count()
                })
                .OrderBy(d => d.Period)
                .ToList();
        }
    }
}