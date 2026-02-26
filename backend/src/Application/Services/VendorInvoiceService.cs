using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Exceptions;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Repositories;
using invoice_v1.src.Services;
using Microsoft.AspNetCore.Http;

namespace invoice_v1.src.Application.Services
{
    public class VendorInvoiceService : IVendorInvoiceService
    {
        private readonly IFileSecurityPipeline _securityPipeline;
        private readonly IGoogleDriveService _driveService;
        private readonly IUserRepository _userRepository;
        private readonly IFileChangeLogRepository _fileChangeLogRepository;
        private readonly IRateLimitService _rateLimitService;
        private readonly IConfiguration _config;
        private readonly ILogger<VendorInvoiceService> _logger;

        public VendorInvoiceService(
            IFileSecurityPipeline securityPipeline,
            IGoogleDriveService driveService,
            IUserRepository userRepository,
            IFileChangeLogRepository fileChangeLogRepository,
            IRateLimitService rateLimitService,
            IConfiguration config,
            ILogger<VendorInvoiceService> logger)
        {
            _securityPipeline = securityPipeline;
            _driveService = driveService;
            _userRepository = userRepository;
            _fileChangeLogRepository = fileChangeLogRepository;
            _rateLimitService = rateLimitService;
            _config = config;
            _logger = logger;
        }

        public async Task<UploadResult> UploadInvoiceAsync(Guid vendorId, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File is required");
            }

            // 1. RATE LIMITING
            var rateLimitKey = $"upload_{vendorId}";
            var maxUploads = _config.GetValue<int>("Security:MaxUploadsPerHour", 20);
            var window = TimeSpan.FromHours(1);

            if (await _rateLimitService.IsRateLimitedAsync(rateLimitKey, maxUploads, window))
            {
                throw new RateLimitExceededException(
                    $"Upload limit of {maxUploads} files per hour exceeded.");
            }

            await _rateLimitService.IncrementAsync(rateLimitKey, window);

            // 2. VENDOR CHECK
            var vendor = await _userRepository.GetByIdAsync(vendorId);
            if (vendor == null || vendor.IsSoftDeleted)
            {
                throw new InvalidOperationException("Vendor not found");
            }

            // 3. SANITIZE FILENAME
            var originalFileName = Path.GetFileName(file.FileName);
            var invalidChars = Path.GetInvalidFileNameChars();
            originalFileName = new string(originalFileName.Where(c => !invalidChars.Contains(c)).ToArray());

            // 4. RUN SECURITY PIPELINE (all checks happen in memory)
            var pipelineResult = await _securityPipeline.RunAsync(file, vendorId);

            // 5. IF UNHEALTHY — no Drive upload, log and reject
            if (!pipelineResult.IsHealthy)
            {
                // DE-DUPLICATION: Check if we already logged this rejection recently (last 30 seconds)
                var recentLog = await _fileChangeLogRepository.GetRecentUnhealthyLogAsync(
                    vendorId, originalFileName, file.Length, TimeSpan.FromSeconds(30));

                if (recentLog == null)
                {
                    var unhealthyLog = new FileChangeLog
                    {
                        FileId = $"rejected_{Guid.NewGuid()}",
                        FileName = originalFileName,
                        ChangeType = "Upload",
                        MimeType = file.ContentType,
                        FileSize = file.Length,
                        UploadedByVendorId = vendorId,
                        DetectedAt = DateTime.UtcNow,
                        Processed = true,
                        ProcessedAt = DateTime.UtcNow,
                        SecurityStatus = nameof(FileSecurityStatus.Unhealthy),
                        SecurityFailReason = pipelineResult.FailReason,
                        SecurityCheckedAt = DateTime.UtcNow
                    };

                    await _fileChangeLogRepository.CreateAsync(unhealthyLog);
                    await _fileChangeLogRepository.SaveChangesAsync();

                    _logger.LogWarning(
                        "SECURITY: File {FileName} from vendor {VendorId} marked UNHEALTHY. Reason: {Reason}",
                        originalFileName, vendorId, pipelineResult.FailReason);
                }
                else
                {
                    _logger.LogInformation(
                        "SECURITY: Redundant rejection for {FileName} (Vendor: {VendorId}). Skipping log creation.",
                        originalFileName, vendorId);
                }

                return new UploadResult
                {
                    Success = false,
                    Message = "File failed security checks.",
                    SecurityReason = pipelineResult.FailReason
                };
            }

            // 6. HEALTHY — upload directly to vendor folder on Drive
            var safeFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
            DriveFileResult driveFile;
            try
            {
                using var uploadStream = file.OpenReadStream();
                driveFile = await _driveService.UploadFileAsync(
                    safeFileName, file.ContentType, uploadStream, vendor.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to upload healthy file {FileName} to Drive for vendor {VendorId}",
                    originalFileName, vendorId);
                throw;
            }

            // 7. Create FileChangeLog with real Drive file ID
            var healthyLog = new FileChangeLog
            {
                FileId = driveFile.Id,
                FileName = originalFileName,
                ChangeType = "Upload",
                MimeType = file.ContentType,
                FileSize = file.Length,
                UploadedByVendorId = vendorId,
                DetectedAt = DateTime.UtcNow,
                Processed = false,
                SecurityStatus = nameof(FileSecurityStatus.Healthy),
                SecurityFailReason = null,
                SecurityCheckedAt = DateTime.UtcNow,
                // SYNC: Capture real modified time from Drive to prevent Monitor bloat
                // FIX: Ensure UTC kind for PostgreSQL compatibility
                GoogleDriveModifiedTime = driveFile.ModifiedTime?.ToUniversalTime()
            };

            await _fileChangeLogRepository.CreateAsync(healthyLog);
            await _fileChangeLogRepository.SaveChangesAsync();

            _logger.LogInformation(
                "File {FileName} uploaded to Drive ({DriveId}) and queued for processing",
                originalFileName, driveFile.Id);

            return new UploadResult
            {
                Success = true,
                FileId = driveFile.Id,
                Message = "File uploaded and queued for processing."
            };
        }
    }
}
