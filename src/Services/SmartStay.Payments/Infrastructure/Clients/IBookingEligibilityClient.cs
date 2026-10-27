using SmartStay.Contracts.Http;

namespace SmartStay.Payments.Infrastructure.Clients;

public interface IBookingEligibilityClient
{
    Task<BookingEligibilityResponse?> CheckEligibilityAsync(int bookingId, CancellationToken ct = default);
}
