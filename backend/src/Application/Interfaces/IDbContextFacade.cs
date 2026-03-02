using Microsoft.EntityFrameworkCore.Storage;

namespace invoice_v1.src.Application.Interfaces
{
    public interface IDbContextFacade
    {
        IExecutionStrategy CreateExecutionStrategy();
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    }
}
