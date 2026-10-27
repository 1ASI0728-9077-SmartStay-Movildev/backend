using Microsoft.EntityFrameworkCore.Storage;
using SmartStay.Profiles.Domain.Repositories;
using SmartStay.Profiles.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Profiles.Infrastructure.Persistence.EFC.Repositories;

public class ProfilesUnitOfWork(ProfilesDbContext context) : IProfilesUnitOfWork
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
