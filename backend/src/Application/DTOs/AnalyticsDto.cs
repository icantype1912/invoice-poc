namespace invoice_v1.src.Application.DTOs
{
    public class ProductSalesDto
    {
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? Category { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal TotalRevenue { get; set; }
        public int InvoiceCount { get; set; }
        public decimal AverageUnitRate { get; set; }
    }

    public class ProductTrendDto
    {
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? Category { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal TotalRevenue { get; set; }
        public int InvoiceCount { get; set; }
        public decimal GrowthRate { get; set; }
        public int Rank { get; set; }
    }

    public class CategorySalesDto
    {
        public string Category { get; set; } = string.Empty;
        public int ProductCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal TotalRevenue { get; set; }
        public int InvoiceCount { get; set; }
        public decimal AverageOrderValue { get; set; }
    }

    public class ProductTimeSeriesDto
    {
        public DateTime Period { get; set; }
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal Revenue { get; set; }
        public int InvoiceCount { get; set; }
    }
    public class RevenueTrendDto
    {
        public DateTime Period { get; set; }
        public decimal Revenue { get; set; }
        public int InvoiceCount { get; set; }
    }
}
