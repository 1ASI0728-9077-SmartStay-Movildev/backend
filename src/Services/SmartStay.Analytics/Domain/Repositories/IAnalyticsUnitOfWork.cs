using Microsoft.EntityFrameworkCore.Storage;

namespace SmartStay.Analytics.Domain.Repositories;

public interface IAnalyticsUnitOfWork
{
    Task CompleteAsync();
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
