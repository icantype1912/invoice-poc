using invoice_v1.src.Domain.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
public class SearchLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? VendorId { get; set; }
    public string NaturalLanguageQuery { get; set; } = string.Empty;
    public string? GeneratedSql { get; set; }
    public SearchLogStatus Status { get; set; }
    public string? RejectionReason { get; set; }
    public int? RowCount { get; set; }
    public long? ExecutionMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public UserRole? User { get; set; }
}
