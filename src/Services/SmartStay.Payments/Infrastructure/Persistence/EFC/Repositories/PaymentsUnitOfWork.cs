using Microsoft.EntityFrameworkCore.Storage;
using SmartStay.Payments.Domain.Repositories;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Payments.Infrastructure.Persistence.EFC.Repositories;

public class PaymentsUnitOfWork(PaymentsDbContext context) : IPaymentsUnitOfWork
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
