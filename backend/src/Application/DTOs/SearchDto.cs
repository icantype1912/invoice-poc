namespace invoice_v1.src.Application.DTOs
{
    public class SearchRequest
    {
        public string Query { get; set; } = string.Empty;
    }

    public class SearchResultDto
    {
        public string NaturalLanguageQuery { get; set; } = string.Empty;
        public string GeneratedSql { get; set; } = string.Empty;
        public List<Dictionary<string, object?>> Rows { get; set; } = new();
        public int RowCount { get; set; }
        public string? Error { get; set; }
        public string? SecurityRejectionReason { get; set; }
    }
}