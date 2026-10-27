using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Configuration;

namespace BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Repositories;

/// <summary>
/// Implementación de la unidad de trabajo exclusiva para AccommodationsDbContext.
/// </summary>
public class AccommodationsUnitOfWork(AccommodationsDbContext context) : IAccommodationsUnitOfWork
{
    public async Task CompleteAsync()
    {
        await context.SaveChangesAsync();
    }
}
