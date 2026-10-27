using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Repositories;

/// <summary>
/// Repository for handling Hotel aggregate persistence against AccommodationsDbContext.
/// </summary>
public class HotelRepository(AccommodationsDbContext context)
    : AccommodationsBaseRepository<Hotel>(context), IHotelRepository
{
    public new async Task<IEnumerable<Hotel>> ListAsync()
    {
        return await Context.Set<Hotel>()
            .Include(h => h.Rooms)
            .ToListAsync();
    }

    public new async Task<Hotel?> FindByIdAsync(int id)
    {
        return await Context.Set<Hotel>()
            .Include(h => h.Rooms)
            .FirstOrDefaultAsync(h => h.Id == id);
    }
}
