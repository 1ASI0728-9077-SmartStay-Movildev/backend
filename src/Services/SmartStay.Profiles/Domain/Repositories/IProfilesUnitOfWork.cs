using Microsoft.EntityFrameworkCore.Storage;

namespace SmartStay.Profiles.Domain.Repositories;

public interface IProfilesUnitOfWork
{
    Task CompleteAsync();
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
