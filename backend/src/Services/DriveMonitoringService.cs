using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Data;
using invoice_v1.src.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using Google.Apis.Drive.v3.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace invoice_v1.src.Services
{
    public class DriveMonitoringService : BackgroundService
    {
        private readonly ILogger<DriveMonitoringService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(59);

        // Thread-safe dictionary to track state in memory
        private readonly ConcurrentDictionary<string, DateTime> _lastSeenFiles = new();
        private readonly ConcurrentDictionary<string, bool> _deletedFiles = new();

        private readonly HashSet<string> _allowedMimeTypes = new()
        {
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "image/jpeg",
            "image/png",
            "text/csv",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };

        private static DateTime ToUtc(DateTime dt) => dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();

        public DriveMonitoringService(ILogger<DriveMonitoringService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Drive Monitoring Service is starting");

            await HydrateDictionaryFromDatabase(stoppingToken);

            using var timer = new PeriodicTimer(_interval);
            try
            {
                await DoWork(stoppingToken);

                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await DoWork(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Drive Monitoring Service is stopping gracefully");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Drive Monitoring Service encountered a fatal error");
            }
        }

        private async Task HydrateDictionaryFromDatabase(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var allLogs = await dbContext.FileChangeLogs
                    .Where(log => log.FileId != null)
                    .GroupBy(log => log.FileId)
                    .Select(group => new
                    {
                        FileId = group.Key,
                        LatestChangeType = group
                            .OrderByDescending(log => log.DetectedAt)
                            .Select(log => log.ChangeType)
                            .FirstOrDefault(),
                        ModifiedTime = group
                            .Where(log => log.GoogleDriveModifiedTime != null &&
                                         (log.ChangeType == "Upload" || log.ChangeType == "Modified"))
                            .OrderByDescending(log => log.GoogleDriveModifiedTime)
                            .Select(log => log.GoogleDriveModifiedTime!.Value)
                            .FirstOrDefault()
                    })
                    .ToListAsync(cancellationToken);

                int activeCount = 0;
                int deletedCount = 0;

                foreach (var file in allLogs.Where(f => f.FileId != null))
                {
                    if (file.LatestChangeType == "Deleted")
                    {
                        _deletedFiles.TryAdd(file.FileId!, true);
                        deletedCount++;
                    }
                    else if (file.ModifiedTime != default || file.LatestChangeType == "Upload")
                    {
                        // If we have a time, track it. If not (API upload), track with min value to trigger DB check logic in DoWork
                        _lastSeenFiles.TryAdd(file.FileId!, file.ModifiedTime != default ? ToUtc(file.ModifiedTime) : DateTime.MinValue);
                        activeCount++;
                    }
                }

                _logger.LogInformation("Hydrated {Active} active and {Deleted} deleted files from DB", activeCount, deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error hydrating dictionary from database");
            }
        }

        private async Task DoWork(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Drive Monitoring check at: {Time}", DateTimeOffset.Now);

            using var scope = _serviceProvider.CreateScope();
            var driveService = scope.ServiceProvider.GetRequiredService<IGoogleDriveService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var folderId = config["GoogleDrive:SharedFolderId"];
            if (string.IsNullOrEmpty(folderId))
            {
                _logger.LogWarning("Shared folder ID not configured");
                return;
            }

            var files = await driveService.ListAllFilesRecursivelyAsync(folderId, cancellationToken);

            if (files.Count == 0 && _lastSeenFiles.IsEmpty)
            {
                _logger.LogInformation("No files found in folder");
                return;
            }

            var allCurrentFiles = files
                .Where(f => f.ModifiedTime.HasValue)
                .ToDictionary(f => f.Id, f => ToUtc(f.ModifiedTime!.Value));

            // PRE-FETCH USERS for Mapping
            var allUsers = await dbContext.Users.ToListAsync(cancellationToken);

            // Map Email -> { Name, Id }
            var userMap = allUsers.ToDictionary(
                u => u.Email!.ToLower(),
                u => new
                {
                    Name = (string?)u.GetType().GetProperty("Name")?.GetValue(u, null) ??
                           (string?)u.GetType().GetProperty("FullName")?.GetValue(u, null) ??
                           (string?)u.GetType().GetProperty("Username")?.GetValue(u, null) ??
                           u.Email,
                    Id = u.Id
                });

            string ResolveModifiedBy(Google.Apis.Drive.v3.Data.File driveFile)
            {
                var owner = driveFile.Owners?.FirstOrDefault();
                if (owner == null) return "Unknown";

                var email = owner.EmailAddress?.ToLower();
                if (!string.IsNullOrEmpty(email) && userMap.TryGetValue(email, out var user))
                {
                    return user.Name!;
                }

                return owner.DisplayName ?? email ?? "Unknown";
            }

            Guid? ResolveVendorId(Google.Apis.Drive.v3.Data.File driveFile)
            {
                var owner = driveFile.Owners?.FirstOrDefault();
                var email = owner?.EmailAddress?.ToLower();
                if (!string.IsNullOrEmpty(email) && userMap.TryGetValue(email, out var user))
                {
                    return user.Id;
                }
                return null;
            }

            var logsToAdd = new List<FileChangeLog>();

            foreach (var file in files)
            {
                if (file.MimeType == null || !_allowedMimeTypes.Contains(file.MimeType)) continue;

                var fileModifiedTime = ToUtc(file.ModifiedTime!.Value);
                var modifiedBy = ResolveModifiedBy(file);
                var vendorId = ResolveVendorId(file);

                // Fetch existing info if possible to preserve FileName and VendorId
                var existingLog = await dbContext.FileChangeLogs
                    .Where(l => l.FileId == file.Id)
                    .OrderByDescending(l => l.DetectedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                var resolvedFileName = existingLog?.FileName ?? file.Name;
                var resolvedVendorId = existingLog?.UploadedByVendorId ?? vendorId;

                // Scenario: Restored
                if (_deletedFiles.ContainsKey(file.Id))
                {
                    _logger.LogInformation("Restored file detected: {FileName}", resolvedFileName);
                    _deletedFiles.TryRemove(file.Id, out _);
                    logsToAdd.Add(CreateLog(file.Id, resolvedFileName, "Upload", fileModifiedTime, modifiedBy, resolvedVendorId, file.MimeType, file.Size));
                    _lastSeenFiles[file.Id] = fileModifiedTime;
                    continue;
                }

                // Scenario: New File (POTENTIAL DUPLICATE)
                if (!_lastSeenFiles.TryGetValue(file.Id, out var lastSeenTime))
                {
                    // CRITICAL: We avoid creating a new log if one already exists for this FileId.
                    // This prevents redundancy with the API upload logs.
                    if (existingLog != null)
                    {
                        _logger.LogDebug("File {FileId} already exists in DB, skipping new log creation", file.Id);
                        _lastSeenFiles.TryAdd(file.Id, fileModifiedTime);
                        continue;
                    }

                    // Only log if it's truly new to both memory and DB.
                    // However, the user specifically asked for DriveMonitoringService to NOT make logs 
                    // that conflict with the security pipeline (VendorInvoiceService).
                    // We will skip "Upload" logs from the monitoring service entirely if we want to be safe,
                    // but usually we want to detect files manually dropped into Drive.
                    // For now, we'll keep the check against existingLog which should satisfy the bloat issue.
                    _logger.LogInformation("New file detected by monitor: {FileName}", resolvedFileName);
                    logsToAdd.Add(CreateLog(file.Id, resolvedFileName, "Upload", fileModifiedTime, modifiedBy, resolvedVendorId, file.MimeType, file.Size));
                    _lastSeenFiles.TryAdd(file.Id, fileModifiedTime);
                }
                // Scenario: Modified File
                // Use a 1-second tolerance to account for precision differences between Drive/DB/Local
                else if (fileModifiedTime > lastSeenTime.AddSeconds(1) && lastSeenTime != DateTime.MinValue)
                {
                    _logger.LogInformation("File modified: {FileName}", resolvedFileName);
                    logsToAdd.Add(CreateLog(file.Id, resolvedFileName, "Modified", fileModifiedTime, modifiedBy, resolvedVendorId, file.MimeType, file.Size));
                    _lastSeenFiles[file.Id] = fileModifiedTime;
                }
                else if (lastSeenTime == DateTime.MinValue)
                {
                    // Was tracked as MinValue during hydration, update to real time now
                    _lastSeenFiles[file.Id] = fileModifiedTime;
                }
            }

            if (logsToAdd.Any())
            {
                dbContext.FileChangeLogs.AddRange(logsToAdd);
            }

            await DetectDeletedFiles(allCurrentFiles, dbContext, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);

            // Sync memory with latest successful state
            foreach (var kvp in allCurrentFiles)
            {
                if (_lastSeenFiles.ContainsKey(kvp.Key))
                {
                    _lastSeenFiles[kvp.Key] = kvp.Value;
                }
            }
        }

        private FileChangeLog CreateLog(
            string fileId,
            string fileName,
            string changeType,
            DateTime modifiedTime,
            string modifiedBy,
            Guid? vendorId,
            string? mimeType,
            long? fileSize)
        {
            return new FileChangeLog
            {
                FileName = fileName,
                FileId = fileId,
                ChangeType = changeType,
                DetectedAt = DateTime.UtcNow,
                MimeType = mimeType,
                FileSize = fileSize,
                ModifiedBy = modifiedBy,
                UploadedByVendorId = vendorId,
                GoogleDriveModifiedTime = modifiedTime,
                Processed = false
            };
        }

        private async Task DetectDeletedFiles(
            Dictionary<string, DateTime> currentFiles,
            ApplicationDbContext dbContext,
            CancellationToken cancellationToken)
        {
            var deletedFileIds = _lastSeenFiles.Keys
                .Where(oldFileId => !currentFiles.ContainsKey(oldFileId))
                .ToList();

            foreach (var oldFileId in deletedFileIds)
            {
                if (!_deletedFiles.ContainsKey(oldFileId))
                {
                    var lastLog = await dbContext.FileChangeLogs
                        .Where(log => log.FileId == oldFileId)
                        .OrderByDescending(log => log.DetectedAt)
                        .FirstOrDefaultAsync(cancellationToken);

                    var changeLog = new FileChangeLog
                    {
                        FileId = oldFileId,
                        FileName = lastLog?.FileName ?? "Unknown",
                        ChangeType = "Deleted",
                        DetectedAt = DateTime.UtcNow,
                        ModifiedBy = lastLog?.ModifiedBy ?? "System",
                        UploadedByVendorId = lastLog?.UploadedByVendorId,
                        Processed = true
                    };

                    dbContext.FileChangeLogs.Add(changeLog);

                    _deletedFiles.TryAdd(oldFileId, true);
                    _lastSeenFiles.TryRemove(oldFileId, out _);

                    _logger.LogInformation("File deleted: {FileId} ({FileName})", oldFileId, lastLog?.FileName);
                }
            }
        }
    }
}
