using BackendAwSmartstay.API.Analytics.Domain.Services.Ports;
using BackendAwSmartstay.API.Bookings.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace BackendAwSmartstay.API.Analytics.Infrastructure.Adapters.Local;

/// <summary>
/// Adaptador de consulta local que lee estadísticas de reservas desde la persistencia compartida durante la transición.
/// </summary>
public class LocalBookingStatsQueryAdapter(AppDbContext context) : IBookingStatsQueryPort
{
    public async Task<(int TotalBookings, int CancelledBookings, int ConfirmedBookings)> GetBookingStatsAsync(DateTime startUtc, DateTime endUtcExclusive)
    {
        var query = context.Set<Booking>()
            .Where(b => b.CheckInDate >= startUtc && b.CheckInDate < endUtcExclusive);

        var total = await query.CountAsync();
        var cancelled = await query.CountAsync(b => b.Status == BookingStatus.Cancelled);
        var confirmed = await query.CountAsync(b => b.Status == BookingStatus.Confirmed);

        return (total, cancelled, confirmed);
    }
}
