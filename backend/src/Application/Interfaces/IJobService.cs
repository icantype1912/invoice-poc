using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Data;
using System.Text.Json;

namespace invoice_v1.src.Application.Interfaces
{
    public interface IJobService
    {
        Task<JobDto?> GetJobByIdAsync(Guid id);
        Task<JobQueue?> GetJobEntityByIdAsync(Guid id);
        Task<(List<JobDto> Jobs, int Total)> GetJobsAsync(JobStatus? status, int page, int pageSize, Guid? vendorId);
        Task<bool> CanVendorAccessJobAsync(Guid jobId, Guid vendorId);

        Task CreateJobFromLogAsync(FileChangeLog log);
        Task CompleteJobAsync(Guid jobId, JsonDocument? result = null);
        Task MarkFailedAsync(Guid jobId, JsonDocument errorDetails);
        Task MarkInvalidAsync(Guid jobId, JsonDocument reasonDetails);
        Task CreateInvalidInvoiceFromJobAsync(Guid jobId, JsonDocument reasonDetails);
        Task RequeueJobAsync(Guid jobId);

        // Added for Retry Service to re-dispatch jobs
        Task ProcessPendingJobAsync(JobQueue job);
    }
}
