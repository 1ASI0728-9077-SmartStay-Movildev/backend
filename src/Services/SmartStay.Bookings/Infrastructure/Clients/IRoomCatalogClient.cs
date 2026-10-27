using SmartStay.Contracts.Http;

namespace SmartStay.Bookings.Infrastructure.Clients;

public interface IRoomCatalogClient
{
    Task<RoomSnapshot?> FindAsync(int roomId, CancellationToken ct = default);
}
