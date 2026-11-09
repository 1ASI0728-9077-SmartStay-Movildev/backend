using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Queries;
using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using BackendAwSmartstay.API.Accommodations.Domain.Services;

namespace BackendAwSmartstay.API.Accommodations.Application.Internal.QueryServices;


public class RoomQueryService(IRoomRepository roomRepository)
    : IRoomQueryService
{
    /// <summary>
    /// Provides query handling services for retrieving room information, 
    /// including fetching rooms by ID, type, or listing all available rooms.
    /// </summary>
    public async Task<Room?> Handle(GetRoomByIdQuery query)
    { /// <summary>
        /// Retrieves a room based on the provided room identifier.
        /// </summary>
        /// <param name="query">Query containing the RoomId to search for.</param>
        /// <returns>The matching <see cref="Room"/> if found; otherwise null.</returns>
        return await roomRepository.FindByIdAsync(query.RoomId);  
       
    }
  
    /// <summary>
    /// Retrieves a list of all rooms available in the system.
    /// </summary>
    /// <param name="query">Query used to trigger the operation.</param>
    /// <returns>An enumerable collection of <see cref="Room"/>.</returns>
    public async Task<IEnumerable<Room>> Handle(GetAllRoomsQuery query)
    {
        return await roomRepository.ListAsync();
    }

    public async Task<IEnumerable<Room>> Handle(GetRoomsByTypeQuery query)
    { 
        /// <summary>
         /// Retrieves all rooms that belong to a specific room type.
        /// </summary>
        /// <param name="query">Query containing the RoomTypeId used for filtering.</param>
        /// <returns>A filtered collection of <see cref="Room"/> that match the requested type.</returns>
        var rooms = await roomRepository.ListAsync();
        return rooms.Where(r => r.RoomTypeId == query.RoomTypeId);
    }

    public async Task<IEnumerable<Room>> SearchRoomsAsync(string? district, int? guests, decimal? minPrice, decimal? maxPrice, double? minArea = null, string? bedType = null)
    {
        var rooms = await roomRepository.ListAsync();
        var result = rooms.Where(r => r.IsBookable);

        if (!string.IsNullOrWhiteSpace(district))
        {
            result = result.Where(r => r.Hotel != null &&
                (!string.IsNullOrWhiteSpace(r.Hotel.District) && r.Hotel.District.Contains(district, StringComparison.OrdinalIgnoreCase) ||
                 !string.IsNullOrWhiteSpace(r.Hotel.City) && r.Hotel.City.Contains(district, StringComparison.OrdinalIgnoreCase)));
        }

        if (guests.HasValue && guests.Value > 0)
        {
            result = result.Where(r => r.Capacity >= guests.Value);
        }

        if (minPrice.HasValue)
        {
            result = result.Where(r => r.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            result = result.Where(r => r.Price <= maxPrice.Value);
        }

        if (minArea.HasValue)
        {
            result = result.Where(r =>
            {
                if (string.IsNullOrWhiteSpace(r.Layout))
                {
                    return false;
                }

                var layout = BackendAwSmartstay.API.Accommodations.Application.Internal.Services.RoomLayoutValidator.Deserialize(r.Layout);
                if (layout is null)
                {
                    return false;
                }

                return layout.WidthM * layout.LengthM >= minArea.Value;
            });
        }

        if (!string.IsNullOrWhiteSpace(bedType))
        {
            result = result.Where(r =>
            {
                if (string.IsNullOrWhiteSpace(r.Layout))
                {
                    return false;
                }

                var layout = BackendAwSmartstay.API.Accommodations.Application.Internal.Services.RoomLayoutValidator.Deserialize(r.Layout);
                if (layout is null)
                {
                    return false;
                }

                return layout.Beds.Any(b => b.Type.Equals(bedType, StringComparison.OrdinalIgnoreCase));
            });
        }

        return result.OrderBy(r => r.Price).ThenBy(r => r.Id).ToList();
    }        

 
}

