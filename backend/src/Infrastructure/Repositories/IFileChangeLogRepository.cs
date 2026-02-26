using invoice_v1.src.Domain.Entities;

namespace invoice_v1.src.Infrastructure.Repositories
{
    public interface IFileChangeLogRepository
    {
        Task<FileChangeLog?> GetByIdAsync(int id);
        Task<FileChangeLog?> GetLatestByFileIdAsync(string fileId);
        Task<List<FileChangeLog>> GetLogsAsync(
            Guid? vendorId,
            string? changeType,
            int skip,
            int take);
        Task<int> GetLogCountAsync(Guid? vendorId, string? changeType);
        Task<List<(string ChangeType, int Count, int Processed, int Pending)>> GetLogStatsAsync(Guid? vendorId);
        Task<List<FileChangeLog>> GetUnprocessedLogsAsync(int limit);
        Task<List<FileChangeLog>> GetUnprocessedHealthyLogsAsync(int limit);
        Task<(List<FileChangeLog> Data, int Total)> GetUnhealthyLogsAsync(int page, int pageSize, Guid? vendorId);
        Task<FileChangeLog?> GetRecentUnhealthyLogAsync(Guid vendorId, string fileName, long fileSize, TimeSpan window);
        Task<FileChangeLog> CreateAsync(FileChangeLog log);
        Task UpdateAsync(FileChangeLog log);
        Task<int> SaveChangesAsync();
    }
}
