using System.Text.Json;

namespace invoice_v1.src.Application.DTOs
{
    public class JobDto
    {
        public Guid Id { get; set; }
        public string JobType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public string? LockedBy { get; set; }
        public DateTime? LockedAt { get; set; }
        public DateTime? NextRetryAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public JsonDocument? PayloadJson { get; set; }
        public JsonDocument? ErrorMessage { get; set; }
        public JsonDocument? ResultJson { get; set; }
    }

    public class JobListResponse
    {
        public List<JobDto> Jobs { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
    }
}
