using invoice_v1.src.Application.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace invoice_v1.src.Infrastructure.Data
{
    public class DbContextFacade : IDbContextFacade
    {
        private readonly ApplicationDbContext _context;

        public DbContextFacade(ApplicationDbContext context) => _context = context;

        public IExecutionStrategy CreateExecutionStrategy() =>
            _context.Database.CreateExecutionStrategy();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            _context.Database.BeginTransactionAsync(cancellationToken);
    }
}
