using Microsoft.EntityFrameworkCore.Storage;
using SmartStay.Bookings.Domain.Repositories;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Bookings.Infrastructure.Persistence.EFC.Repositories;

public class BookingsUnitOfWork(BookingsDbContext context) : IBookingsUnitOfWork
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
