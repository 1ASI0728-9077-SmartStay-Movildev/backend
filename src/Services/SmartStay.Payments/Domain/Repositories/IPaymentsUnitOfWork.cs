using Microsoft.EntityFrameworkCore.Storage;

namespace SmartStay.Payments.Domain.Repositories;

public interface IPaymentsUnitOfWork
{
    Task CompleteAsync();
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
