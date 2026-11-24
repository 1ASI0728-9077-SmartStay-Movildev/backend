using SmartStay.Contracts.Http;

namespace SmartStay.Concierge.Infrastructure.Clients;

public sealed record QuoteRequestResult(bool Success, BookingQuoteResponse? Quote, string? ErrorMessage);

public sealed record BookingReservationResult(bool Success, int? BookingId, bool IsTransientError, string? ErrorMessage);

public interface IBookingsClient
{
    Task<QuoteRequestResult> RequestQuoteAsync(CreateBookingQuoteRequest request, CancellationToken ct = default);
    Task<BookingByIntentResponse?> GetBookingByIntentIdAsync(string intentId, CancellationToken ct = default);
    Task<BookingReservationResult> CreateBookingAsync(
        int roomId,
        string guestName,
        string guestEmail,
        DateTime checkIn,
        DateTime checkOut,
        string quoteId,
        string purchaseIntentId,
        int guestUserId,
        string idempotencyKey,
        CancellationToken ct = default);
    Task<bool> CancelBookingAsync(int bookingId, string reason, int guestUserId, CancellationToken ct = default);
}
