using BackendAwSmartstay.API.Analytics.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Analytics.Domain.Repositories;
using BackendAwSmartstay.API.Analytics.Domain.Services.Ports;

namespace BackendAwSmartstay.API.Analytics.Infrastructure.Persistence.EFC.Repositories;

/// <summary>
/// Repositorio de analítica desacoplado mediante puertos de consulta.
/// Durante la transición utiliza adaptadores locales; al extraer servicios se sustituyen por adaptadores HTTP o proyecciones asíncronas.
/// </summary>
public class AnalyticsRepository(
    IRevenueQueryPort revenueQueryPort,
    IBookingStatsQueryPort bookingStatsQueryPort,
    ICatalogQueryPort catalogQueryPort) : IAnalyticsRepository
{
    public async Task<PerformanceMetrics> GetMonthlyMetricsAsync()
    {
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfMonthExclusive = startOfMonth.AddMonths(1);

        // 1. Ingresos totales (intervalo exacto [inicioMes, inicioMesSiguiente))
        var totalRevenue = await revenueQueryPort.GetTotalRevenueAsync(startOfMonth, endOfMonthExclusive);

        // 2. Estadísticas de reservas del mes
        var (totalBookings, cancelledBookings, confirmedBookings) =
            await bookingStatsQueryPort.GetBookingStatsAsync(startOfMonth, endOfMonthExclusive);

        // 3. Tasa de ocupación
        var totalRooms = await catalogQueryPort.GetTotalRoomsCountAsync();

        double occupancyRate = 0;
        if (totalRooms > 0 && totalBookings > 0)
        {
            occupancyRate = ((double)confirmedBookings / totalRooms) * 100;
        }

        return new PerformanceMetrics
        {
            TotalRevenue = totalRevenue,
            TotalBookings = totalBookings,
            CancelledBookings = cancelledBookings,
            OccupancyRate = Math.Round(occupancyRate, 2)
        };
    }
}
