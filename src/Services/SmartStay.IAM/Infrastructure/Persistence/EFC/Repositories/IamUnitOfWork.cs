using Microsoft.EntityFrameworkCore.Storage;
using SmartStay.IAM.Domain.Repositories;
using SmartStay.IAM.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.IAM.Infrastructure.Persistence.EFC.Repositories;

public class IamUnitOfWork(IamDbContext context) : IIamUnitOfWork
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
