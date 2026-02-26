using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace invoice_v1.src.Infrastructure.Repositories
{
    public class JobRepository : IJobRepository
    {
        private readonly ApplicationDbContext _context;

        public JobRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<JobQueue?> GetByIdAsync(Guid id)
        {
            return await _context.JobQueues
                .FirstOrDefaultAsync(j => j.Id == id);
        }

        public async Task<List<JobQueue>> GetJobsByFileIdAsync(string fileId)
        {
            return await _context.JobQueues
                .Where(j => j.PayloadJson.RootElement.GetProperty("fileId").GetString() == fileId)
                .OrderByDescending(j => j.CreatedAt)
                .ToListAsync();
        }

        public async Task<(List<JobQueue> jobs, int total)> GetJobsAsync(
            JobStatus? status,
            int page,
            int pageSize,
            Guid? vendorId)
        {
            var query = _context.JobQueues.AsQueryable();

            if (status.HasValue)
            {
                var statusString = status.Value.ToString();
                query = query.Where(j => j.Status == statusString);
            }

            if (vendorId.HasValue)
            {
                var vendorIdString = vendorId.Value.ToString();
                query = query.Where(j =>
                    j.PayloadJson.RootElement.GetProperty("uploader").GetString() == vendorIdString);
            }

            var total = await query.CountAsync();

            var jobs = await query
                .OrderByDescending(j => j.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (jobs, total);
        }

        public async Task CreateAsync(JobQueue job)
        {
            await _context.JobQueues.AddAsync(job);
        }

        public async Task UpdateAsync(JobQueue job)
        {
            _context.JobQueues.Update(job);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
