using Microsoft.EntityFrameworkCore.Storage;
using SmartStay.Analytics.Domain.Repositories;
using SmartStay.Analytics.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Analytics.Infrastructure.Persistence.EFC.Repositories;

public class AnalyticsUnitOfWork(AnalyticsDbContext context) : IAnalyticsUnitOfWork
{
    public async Task CompleteAsync()
    {
        await context.SaveChangesAsync();
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        return await context.Database.BeginTransactionAsync(ct);
    }
}
