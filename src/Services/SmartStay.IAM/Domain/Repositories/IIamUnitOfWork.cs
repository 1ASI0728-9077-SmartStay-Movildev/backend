using Microsoft.EntityFrameworkCore.Storage;

namespace SmartStay.IAM.Domain.Repositories;

public interface IIamUnitOfWork
{
    Task CompleteAsync();
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
