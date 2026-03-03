using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace invoice_v1.src.Domain.Entities
{
    /// <summary>
    /// Represents a job in the processing queue.
    /// Jobs are created by the backend from FileChangeLogs and claimed by workers.
    /// </summary>
    public class JobQueue
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(50)]
        public string JobType { get; set; } = nameof(Enums.JobType.INVOICE_EXTRACTION);

        /// <summary>
        /// JSON payload containing fileId, originalName, mimeType, etc.
        /// Stored as jsonb in PostgreSQL.
        /// </summary>
        [Required]
        public JsonDocument PayloadJson { get; set; } = JsonDocument.Parse("{}");

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = nameof(Enums.JobStatus.PENDING);

        public int RetryCount { get; set; } = 0;

        [MaxLength(200)]
        public string? LockedBy { get; set; }

        public DateTime? LockedAt { get; set; }

        public DateTime? NextRetryAt { get; set; }

        /// <summary>
        /// Structured error information (exception, stack trace, worker id, etc.)
        /// Stored as jsonb.
        /// </summary>
        public JsonDocument? ErrorMessage { get; set; }

        /// <summary>
        /// Raw extraction result from the AI worker.
        /// Stored as jsonb.
        /// </summary>
        public JsonDocument? ResultJson { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
