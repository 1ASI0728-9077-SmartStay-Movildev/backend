using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Queries;
using SmartStay.Bookings.Domain.Services;
using SmartStay.Contracts.Http;

namespace SmartStay.Bookings.Interfaces.REST;

/// <summary>
/// Endpoint interno de Bookings para consultas de otros microservicios (Payments y Analytics).
/// </summary>
[ApiController]
[Route("internal/v1/bookings")]
[AllowAnonymous]
public class InternalBookingsController(
    IBookingQueryService bookingQueryService) : ControllerBase
{
    [HttpGet("{bookingId:int}/eligibility")]
    public async Task<IActionResult> GetBookingEligibility(int bookingId)
    {
        var booking = await bookingQueryService.Handle(new GetBookingByIdQuery(bookingId));
        if (booking is null) return NotFound();

        var isNotExpired = booking.ExpiresAtUtc == null || booking.ExpiresAtUtc > DateTime.UtcNow;
        var isEligible = booking.Status == BookingStatus.PendingPayment && isNotExpired;

        var response = new BookingEligibilityResponse(
            BookingId: booking.Id,
            RoomId: booking.RoomId,
            HotelId: booking.HotelId,
            TotalAmount: booking.TotalAmount,
            Currency: booking.Currency,
            Status: booking.Status.ToString(),
            IsEligibleForPayment: isEligible
        );

        return Ok(response);
    }

    [HttpGet("stats/monthly")]
    public async Task<IActionResult> GetMonthlyStats([FromQuery] DateTime startUtc, [FromQuery] DateTime endUtcExclusive)
    {
        var allBookings = await bookingQueryService.Handle(new GetAllBookingsQuery());
        var monthly = allBookings
            .Where(b => b.CheckInDate >= startUtc && b.CheckInDate < endUtcExclusive)
            .ToList();

        var total = monthly.Count;
        var cancelled = monthly.Count(b => b.Status == BookingStatus.Cancelled);
        var confirmed = monthly.Count(b => b.Status == BookingStatus.Confirmed);

        return Ok(new { Total = total, Cancelled = cancelled, Confirmed = confirmed });
    }

    [HttpPost("/internal/v1/availability/check")]
    public async Task<IActionResult> CheckAvailability([FromBody] AvailabilityRequest request)
    {
        if (request.CheckIn >= request.CheckOut)
        {
            return BadRequest(new { message = "CheckIn date must be before CheckOut date." });
        }

        var nights = request.CheckOut.DayNumber - request.CheckIn.DayNumber;
        if (nights is < 1 or > 30)
        {
            return BadRequest(new { message = "Stay duration must be between 1 and 30 nights for MVP." });
        }

        var response = await bookingQueryService.CheckAvailabilityAsync(request);
        return Ok(response);
    }
}
