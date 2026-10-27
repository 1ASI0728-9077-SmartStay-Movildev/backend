using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using SmartStay.Accommodations.Infrastructure.Persistence.EFC.Configuration;

namespace BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Repositories;

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
