using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Domain.Enums;

namespace invoice_v1.src.Application.Interfaces
{
    public interface IAnalyticsService
    {
        Task<List<ProductSalesDto>> GetProductSalesByDateRangeAsync(
            DateTime startDate,
            DateTime endDate,
            string? category = null,
            Guid? vendorId = null);

        Task<List<ProductTrendDto>> GetTrendingProductsAsync(
            DateTime startDate,
            DateTime endDate,
            int topN = 10,
            Guid? vendorId = null);

        Task<List<CategorySalesDto>> GetCategorySalesAsync(
            DateTime startDate,
            DateTime endDate,
            Guid? vendorId = null);

        Task<List<ProductTimeSeriesDto>> GetProductTimeSeriesAsync(
            string productId,
            DateTime startDate,
            DateTime endDate,
            TimeGranularity granularity = TimeGranularity.Monthly,
            Guid? vendorId = null);

        Task<List<RevenueTrendDto>> GetRevenueTrendAsync(
            DateTime startDate,
            DateTime endDate,
            TimeGranularity granularity = TimeGranularity.Monthly,
            Guid? vendorId = null);
    }
}
