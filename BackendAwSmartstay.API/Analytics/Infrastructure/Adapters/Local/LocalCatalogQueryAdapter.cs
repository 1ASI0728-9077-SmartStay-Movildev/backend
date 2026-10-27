using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Analytics.Domain.Services.Ports;
using BackendAwSmartstay.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace BackendAwSmartstay.API.Analytics.Infrastructure.Adapters.Local;

/// <summary>
/// Adaptador de consulta local que lee inventario de habitaciones desde la persistencia compartida durante la transición.
/// </summary>
public class LocalCatalogQueryAdapter(AppDbContext context) : ICatalogQueryPort
{
    public async Task<int> GetTotalRoomsCountAsync()
    {
        return await context.Set<Room>().CountAsync();
    }
}
