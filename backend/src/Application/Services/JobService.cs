using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Repositories;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace invoice_v1.src.Application.Services
{
    public class JobService : IJobService
    {
        private readonly IJobRepository _jobQueueRepository;
        private readonly IInvalidInvoiceRepository _invalidInvoiceRepository;
        private readonly ILogger<JobService> _logger;

        // CONFIGURATION: Max retries allowed before marking as INVALID
        private const int MaxRetries = 3;

        public JobService(
            IJobRepository jobQueueRepository,
            IInvalidInvoiceRepository invalidInvoiceRepository,
            ILogger<JobService> logger)
        {
            _jobQueueRepository = jobQueueRepository;
            _invalidInvoiceRepository = invalidInvoiceRepository;
            _logger = logger;
        }

        public async Task<JobDto?> GetJobByIdAsync(Guid id)
        {
            var job = await _jobQueueRepository.GetByIdAsync(id);
            return job == null ? null : MapToDto(job);
        }

        public async Task<JobQueue?> GetJobEntityByIdAsync(Guid id)
        {
            return await _jobQueueRepository.GetByIdAsync(id);
        }

        public async Task<(List<JobDto> Jobs, int Total)> GetJobsAsync(
            JobStatus? status,
            int page,
            int pageSize,
            Guid? vendorId)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            var (jobs, total) = await _jobQueueRepository.GetJobsAsync(status, page, pageSize, vendorId);
            var jobDtos = jobs.Select(MapToDto).ToList();

            return (jobDtos, total);
        }

        public async Task<bool> CanVendorAccessJobAsync(Guid jobId, Guid vendorId)
        {
            var job = await _jobQueueRepository.GetByIdAsync(jobId);
            if (job == null) return false;

            var payload = job.PayloadJson?.RootElement;
            if (payload == null) return false;

            if (payload.Value.TryGetProperty("uploader", out var vendorIdProp))
            {
                var jobVendorIdStr = vendorIdProp.GetString();
                if (Guid.TryParse(jobVendorIdStr, out var jobVendorId))
                {
                    return jobVendorId == vendorId;
                }
            }
            return false;
        }

        public async Task CreateJobFromLogAsync(FileChangeLog log)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));
            if (string.IsNullOrWhiteSpace(log.FileId)) throw new ArgumentException("FileId cannot be empty", nameof(log));
            if (string.IsNullOrWhiteSpace(log.FileName)) throw new ArgumentException("FileName cannot be empty", nameof(log));

            // Check if job already exists (idempotency)
            var existingJobs = await _jobQueueRepository.GetJobsByFileIdAsync(log.FileId);

            // CORRECTED: Removed JobStatus.CANCELLED check
            if (existingJobs.Any(j => j.Status != nameof(JobStatus.FAILED) && j.Status != nameof(JobStatus.INVALID)))
            {
                _logger.LogWarning("Active Job already exists for FileId {FileId}, skipping creation", log.FileId);
                return;
            }

            var payload = new
            {
                fileId = log.FileId,
                originalName = log.FileName,
                mimeType = log.MimeType ?? "application/octet-stream",
                fileSize = log.FileSize ?? 0,
                uploader = log.UploadedByVendorId?.ToString(),
                schemaVersion = "1.0",
                idempotencyKey = $"{log.FileId}_{log.DetectedAt:yyyyMMddHHmmss}",
                detectedAt = log.DetectedAt.ToString("o")
            };

            var job = new JobQueue
            {
                Id = Guid.NewGuid(),
                JobType = nameof(JobType.INVOICE_EXTRACTION),
                Status = nameof(JobStatus.PENDING),
                PayloadJson = JsonSerializer.SerializeToDocument(payload),
                RetryCount = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _jobQueueRepository.CreateAsync(job);
            await _jobQueueRepository.SaveChangesAsync();

            _logger.LogInformation("Created job {JobId} for file {FileId} {FileName}", job.Id, log.FileId, log.FileName);

            // Worker will pick up the PENDING job via its polling loop —
            // no push notification needed.
        }

        public async Task CompleteJobAsync(Guid jobId)
        {
            var job = await _jobQueueRepository.GetByIdAsync(jobId);
            if (job == null) throw new InvalidOperationException($"Job {jobId} not found");

            job.Status = nameof(JobStatus.COMPLETED);
            job.UpdatedAt = DateTime.UtcNow;

            // Cleanup locks
            job.LockedBy = null;
            job.LockedAt = null;

            await _jobQueueRepository.UpdateAsync(job);
            await _jobQueueRepository.SaveChangesAsync();

            _logger.LogInformation("Job {JobId} marked as completed", jobId);
        }

        public async Task MarkFailedAsync(Guid jobId, JsonDocument errorDetails)
        {
            var job = await _jobQueueRepository.GetByIdAsync(jobId);
            if (job == null) throw new InvalidOperationException($"Job {jobId} not found");

            // Final State: INVALID (was FAILED)
            // The worker handles retry scheduling internally (Status=PENDING).
            // If the worker sends a FAILED callback, it means it's a permanent failure 
            // or max retries were reached.
            // We transition this to INVALID so it appears in the Invalid Invoices view.
            job.Status = nameof(JobStatus.INVALID);
            job.ErrorMessage = errorDetails;
            job.NextRetryAt = null;
            job.LockedBy = null;
            job.LockedAt = null;
            job.UpdatedAt = DateTime.UtcNow;

            await _jobQueueRepository.UpdateAsync(job);
            await _jobQueueRepository.SaveChangesAsync();

            // Create entry in invalid_invoice for user visibility
            await CreateInvalidInvoiceFromJobAsync(jobId, errorDetails);

            _logger.LogError("Job {JobId} reached permanent failure and was marked as INVALID. Reason: {Error}", 
                jobId, errorDetails.RootElement.ToString());
        }

        public async Task MarkInvalidAsync(Guid jobId, JsonDocument reasonDetails)
        {
            var job = await _jobQueueRepository.GetByIdAsync(jobId);
            if (job == null) throw new InvalidOperationException($"Job {jobId} not found");

            job.Status = nameof(JobStatus.INVALID);
            job.ErrorMessage = reasonDetails;

            // Clear retry info
            job.NextRetryAt = null;
            job.LockedBy = null;
            job.LockedAt = null;
            job.UpdatedAt = DateTime.UtcNow;

            await _jobQueueRepository.UpdateAsync(job);
            await _jobQueueRepository.SaveChangesAsync();

            _logger.LogInformation("Job {JobId} marked as INVALID", jobId);
        }

        public async Task CreateInvalidInvoiceFromJobAsync(Guid jobId, JsonDocument reasonDetails)
        {
            var job = await _jobQueueRepository.GetByIdAsync(jobId);
            if (job == null) return;

            var payload = job.PayloadJson?.RootElement;
            if (payload == null)
            {
                _logger.LogWarning("Job {JobId} has no payload, cannot create invalid invoice", jobId);
                return;
            }

            string? fileId = null;
            if (payload.Value.TryGetProperty("fileId", out var fidProp)) fileId = fidProp.GetString();

            string? fileName = "Unknown";
            if (payload.Value.TryGetProperty("originalName", out var fnameProp)) fileName = fnameProp.GetString();

            Guid? vendorId = null;
            if (payload.Value.TryGetProperty("uploader", out var vidProp) && Guid.TryParse(vidProp.GetString(), out var vid))
            {
                vendorId = vid;
            }

            if (string.IsNullOrWhiteSpace(fileId))
            {
                _logger.LogWarning("Job {JobId} has no fileId in payload", jobId);
                return;
            }

            var invalidInvoice = new InvalidInvoice
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                FileId = fileId,
                FileName = fileName,
                VendorId = vendorId,
                Reason = reasonDetails,
                CreatedAt = DateTime.UtcNow
            };

            await _invalidInvoiceRepository.CreateAsync(invalidInvoice);
            await _invalidInvoiceRepository.SaveChangesAsync();

            _logger.LogInformation("Created invalid invoice {InvoiceId} for job {JobId}", invalidInvoice.Id, jobId);
        }

        public async Task RequeueJobAsync(Guid jobId)
        {
            var job = await _jobQueueRepository.GetByIdAsync(jobId);
            if (job == null) throw new InvalidOperationException($"Job {jobId} not found");

            // CORRECTED: Removed JobStatus.CANCELLED check
            if (job.Status != nameof(JobStatus.FAILED) && job.Status != nameof(JobStatus.INVALID))
            {
                throw new InvalidOperationException($"Job {jobId} cannot be requeued. Current status: {job.Status}");
            }

            // 1. CLEANUP: Remove from Invalid Invoices table
            await _invalidInvoiceRepository.DeleteByJobIdAsync(jobId);

            // 2. RESET: Reset job state
            job.Status = nameof(JobStatus.PENDING);
            job.ErrorMessage = null;
            job.RetryCount = 0;
            job.NextRetryAt = null;
            job.LockedBy = null;
            job.LockedAt = null;
            job.UpdatedAt = DateTime.UtcNow;

            await _jobQueueRepository.UpdateAsync(job);
            await _jobQueueRepository.SaveChangesAsync();

            _logger.LogInformation("Job {JobId} manually requeued by admin", jobId);

            // 3. TRIGGER: Attempt immediate dispatch
            await ProcessPendingJobAsync(job);
        }

        public async Task ProcessPendingJobAsync(JobQueue job)
        {
            // Clear NextRetryAt so the worker's poll query picks this job up
            // (worker claims where NextRetryAt IS NULL OR <= NOW)
            job.NextRetryAt = null;
            await _jobQueueRepository.UpdateAsync(job);
            await _jobQueueRepository.SaveChangesAsync();

            _logger.LogInformation(
                "Job {JobId} (attempt {RetryCount}) is PENDING — worker will pick it up on next poll",
                job.Id, job.RetryCount);
        }

        private static JobDto MapToDto(JobQueue job)
        {
            return new JobDto
            {
                Id = job.Id,
                JobType = job.JobType,
                Status = job.Status,
                PayloadJson = job.PayloadJson,
                ErrorMessage = job.ErrorMessage,
                RetryCount = job.RetryCount,
                LockedBy = job.LockedBy,
                LockedAt = job.LockedAt,
                NextRetryAt = job.NextRetryAt,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt
            };
        }
    }
}
