using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Commands;
using SmartStay.Bookings.Domain.Model.Exceptions;
using SmartStay.Bookings.Domain.Model.Queries;
using SmartStay.Bookings.Domain.Services;
using SmartStay.Contracts.Http;

namespace SmartStay.Bookings.Interfaces.REST;

/// <summary>
/// Endpoint interno de Bookings para consultas de otros microservicios (Payments, Analytics y Concierge).
/// </summary>
[ApiController]
[Route("internal/v1/bookings")]
[AllowAnonymous]
public class InternalBookingsController(
    IBookingQueryService bookingQueryService,
    IBookingCommandService bookingCommandService) : ControllerBase
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

    [HttpPost("quotes")]
    public async Task<IActionResult> CreateQuote([FromBody] CreateBookingQuoteRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { message = "Request body cannot be null." });
        }

        if (!DateOnly.TryParse(request.CheckIn, out var checkIn) || !DateOnly.TryParse(request.CheckOut, out var checkOut))
        {
            return BadRequest(new { message = "Dates must be in valid format (yyyy-MM-dd)." });
        }

        if (checkIn >= checkOut)
        {
            return BadRequest(new { message = "CheckIn date must be before CheckOut date." });
        }

        var nights = checkOut.DayNumber - checkIn.DayNumber;
        if (nights is < 1 or > 30)
        {
            return BadRequest(new { message = "Stay duration must be between 1 and 30 nights." });
        }

        try
        {
            var command = new CreateBookingQuoteCommand(
                request.RoomId,
                request.GuestUserId,
                request.PurchaseIntentId,
                checkIn,
                checkOut,
                request.Guests
            );

            var quote = await bookingCommandService.Handle(command);
            if (quote is null)
            {
                return NotFound(new { message = $"Room #{request.RoomId} was not found." });
            }

            var response = new BookingQuoteResponse(
                QuoteId: quote.QuoteId,
                RoomId: quote.RoomId,
                StayTotal: quote.StayTotal,
                Currency: quote.Currency,
                ExpiresAtUtc: quote.ExpiresAtUtc
            );

            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (RoomCapacityExceededException ex)
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new { message = ex.Message, reason = "CapacityExceeded" });
        }
        catch (RoomNotBookableException ex)
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new { message = ex.Message, reason = "NotBookable" });
        }
        catch (RoomAlreadyBookedException ex)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { message = ex.Message, roomId = ex.RoomId });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("by-intent/{intentId}")]
    public async Task<IActionResult> GetBookingByIntent(string intentId)
    {
        if (string.IsNullOrWhiteSpace(intentId))
        {
            return BadRequest(new { message = "IntentId is required." });
        }

        var booking = await bookingQueryService.Handle(new GetBookingByPurchaseIntentIdQuery(intentId));
        if (booking is null)
        {
            return NotFound(new { message = $"No booking found for purchase intent '{intentId}'." });
        }

        var response = new BookingByIntentResponse(
            BookingId: booking.Id,
            RoomId: booking.RoomId,
            HotelId: booking.HotelId,
            GuestUserId: booking.GuestUserId,
            TotalAmount: booking.TotalAmount,
            Currency: booking.Currency,
            Status: booking.Status.ToString(),
            ExpiresAtUtc: booking.ExpiresAtUtc,
            PaymentId: booking.PaymentId,
            CreatedAtUtc: booking.CreatedAtUtc
        );

        return Ok(response);
    }
}
