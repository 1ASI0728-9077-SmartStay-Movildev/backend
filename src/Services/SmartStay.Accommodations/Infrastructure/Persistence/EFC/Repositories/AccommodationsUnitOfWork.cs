using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using SmartStay.Accommodations.Infrastructure.Persistence.EFC.Configuration;

namespace BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Repositories;

public class AccommodationsUnitOfWork(AccommodationsDbContext context) : IAccommodationsUnitOfWork
{
    public async Task CompleteAsync()
    {
        await context.SaveChangesAsync();
    }
}
