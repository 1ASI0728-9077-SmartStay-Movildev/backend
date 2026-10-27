using BackendAwSmartstay.API.Accommodations.Domain.Model.Queries;
using BackendAwSmartstay.API.Accommodations.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Contracts.Http;

namespace SmartStay.Accommodations.Interfaces.REST;

/// <summary>
/// Endpoint interno de consulta de catálogo para Bookings.
/// Protegido a nivel perimetral de Nginx.
/// </summary>
[ApiController]
[Route("internal/v1/rooms")]
[AllowAnonymous]
public class InternalRoomsController(IRoomQueryService roomQueryService) : ControllerBase
{
    [HttpGet("{roomId:int}")]
    public async Task<IActionResult> GetRoomSnapshot(int roomId)
    {
        var room = await roomQueryService.Handle(new GetRoomByIdQuery(roomId));
        if (room is null) return NotFound();

        var snapshot = new RoomSnapshot(
            RoomId: room.Id,
            HotelId: room.HotelId,
            IsBookable: true,
            NightlyPrice: room.Price,
            Currency: "USD"
        );

        return Ok(snapshot);
    }
}
