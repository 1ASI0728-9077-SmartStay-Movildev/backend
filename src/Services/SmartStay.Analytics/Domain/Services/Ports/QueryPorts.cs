namespace SmartStay.Analytics.Domain.Services.Ports;

public interface IRevenueQueryPort
{
    Task<decimal> GetTotalRevenueAsync(DateTime startUtc, DateTime endUtcExclusive);
}

public interface IBookingStatsQueryPort
{
    Task<(int TotalBookings, int CancelledBookings, int ConfirmedBookings)> GetBookingStatsAsync(DateTime startUtc, DateTime endUtcExclusive);
}

public interface ICatalogQueryPort
{
    Task<int> GetTotalRoomsCountAsync();
}
