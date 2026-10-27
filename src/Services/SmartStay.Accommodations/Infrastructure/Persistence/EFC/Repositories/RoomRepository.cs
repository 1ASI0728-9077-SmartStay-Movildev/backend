using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using SmartStay.Accommodations.Infrastructure.Persistence.EFC.Configuration;

namespace BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Repositories;

public class RoomRepository(AccommodationsDbContext context)
    : AccommodationsBaseRepository<Room>(context), IRoomRepository
{
    public new async Task<Room?> FindByIdAsync(int id)
    {
        return await Context.Set<Room>()
            .Include(r => r.RoomType) 
            .Include(r => r.Hotel) 
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public new async Task<IEnumerable<Room>> ListAsync()
    {
        return await Context.Set<Room>()
            .Include(r => r.RoomType)
            .Include(r => r.Hotel)
            .ToListAsync();
    }
}
