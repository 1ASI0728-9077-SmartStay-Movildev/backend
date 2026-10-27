using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Bookings.Domain.Model.Commands;
using SmartStay.Bookings.Domain.Model.Exceptions;
using SmartStay.Bookings.Domain.Model.Queries;
using SmartStay.Bookings.Domain.Services;
using SmartStay.Bookings.Infrastructure.Security;
using SmartStay.Bookings.Interfaces.REST.Resources;
using SmartStay.Bookings.Interfaces.REST.Transform;
using SmartStay.Contracts.Constants;
using Swashbuckle.AspNetCore.Annotations;

namespace SmartStay.Bookings.Interfaces.REST;

/// <summary>
/// RESTful API interface controller for Bookings microservice.
/// </summary>
[BookingsAuthorize]
[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Available Booking Endpoints")]
public class BookingsController(
    IBookingCommandService bookingCommandService,
    IBookingQueryService bookingQueryService) : ControllerBase
{
    [HttpGet("mine")]
    [BookingsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get bookings of the currently authenticated guest", OperationId = "GetMyBookings")]
    [SwaggerResponse(StatusCodes.Status200OK, "List of guest bookings", typeof(IEnumerable<BookingResource>))]
    public async Task<IActionResult> GetMyBookings()
    {
        var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor is null || actor.UserId <= 0) return Unauthorized();

        var bookings = await bookingQueryService.GetBookingsByGuestUserIdAsync(actor.UserId);
        return Ok(bookings.Select(BookingAssemblers.ToResource));
    }

    [HttpGet("{bookingId:int}")]
    [BookingsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get booking by its unique identifier", OperationId = "GetBookingById")]
    [SwaggerResponse(StatusCodes.Status200OK, "Booking found", typeof(BookingResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Booking not found")]
    public async Task<IActionResult> GetBookingById(int bookingId)
    {
        var booking = await bookingQueryService.Handle(new GetBookingByIdQuery(bookingId));
        if (booking is null) return NotFound();

        var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor != null && !actor.IsHotelAdmin && !actor.IsChainAdmin && booking.GuestUserId > 0 && booking.GuestUserId != actor.UserId)
        {
            return Forbid();
        }

        return Ok(BookingAssemblers.ToResource(booking));
    }

    [HttpPost]
    [BookingsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Create a new reservation entry", OperationId = "CreateBooking")]
    [SwaggerResponse(StatusCodes.Status201Created, "Booking successfully created", typeof(BookingResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid booking parameters")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "Room is already booked for requested dates")]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingResource resource)
    {
        try
        {
            var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
            var command = BookingAssemblers.ToCommand(resource, actor?.UserId ?? 0);
            var booking = await bookingCommandService.Handle(command);
            if (booking is null) return BadRequest("Could not create booking. Please verify room existence and dates.");

            var resourceResult = BookingAssemblers.ToResource(booking);
            return CreatedAtAction(nameof(GetBookingById), new { bookingId = booking.Id }, resourceResult);
        }
        catch (RoomAlreadyBookedException ex)
        {
            return Conflict(new { error = ex.Message, roomId = ex.RoomId, collisionDate = ex.CollisionDate.ToString("yyyy-MM-dd") });
        }
    }

    [HttpGet]
    [BookingsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get all tracked reservations", OperationId = "GetAllBookings")]
    [SwaggerResponse(StatusCodes.Status200OK, "List of all bookings", typeof(IEnumerable<BookingResource>))]
    public async Task<IActionResult> GetAllBookings()
    {
        var bookings = await bookingQueryService.Handle(new GetAllBookingsQuery());
        return Ok(bookings.Select(BookingAssemblers.ToResource));
    }

    [HttpGet("room/{roomId:int}")]
    [BookingsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get bookings by room", OperationId = "GetBookingsByRoomId")]
    [SwaggerResponse(StatusCodes.Status200OK, "List of bookings for room", typeof(IEnumerable<BookingResource>))]
    public async Task<IActionResult> GetBookingsByRoomId(int roomId)
    {
        var bookings = await bookingQueryService.Handle(new GetBookingsByRoomIdQuery(roomId));
        return Ok(bookings.Select(BookingAssemblers.ToResource));
    }

    [HttpPost("{bookingId:int}/confirm")]
    [BookingsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Confirm reservation", OperationId = "ConfirmBooking")]
    [SwaggerResponse(StatusCodes.Status200OK, "Booking confirmed", typeof(BookingResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Booking not found")]
    public async Task<IActionResult> ConfirmBooking(int bookingId)
    {
        var booking = await bookingCommandService.Handle(new ConfirmBookingCommand(bookingId));
        if (booking is null) return NotFound();
        return Ok(BookingAssemblers.ToResource(booking));
    }

    [HttpPost("{bookingId:int}/cancel")]
    [BookingsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Cancel reservation", OperationId = "CancelBooking")]
    [SwaggerResponse(StatusCodes.Status200OK, "Booking cancelled", typeof(BookingResource))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Forbidden to cancel another user's reservation")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Booking not found")]
    public async Task<IActionResult> CancelBooking(int bookingId)
    {
        var existing = await bookingQueryService.Handle(new GetBookingByIdQuery(bookingId));
        if (existing is null) return NotFound();

        var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor != null && !actor.IsHotelAdmin && !actor.IsChainAdmin && existing.GuestUserId > 0 && existing.GuestUserId != actor.UserId)
        {
            return Forbid();
        }

        var booking = await bookingCommandService.Handle(new CancelBookingCommand(bookingId));
        if (booking is null) return NotFound();
        return Ok(BookingAssemblers.ToResource(booking));
    }
}
