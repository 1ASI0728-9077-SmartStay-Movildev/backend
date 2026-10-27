namespace SmartStay.Bookings.Domain.Repositories;

public interface IBookingsUnitOfWork
{
    Task CompleteAsync();
    Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
