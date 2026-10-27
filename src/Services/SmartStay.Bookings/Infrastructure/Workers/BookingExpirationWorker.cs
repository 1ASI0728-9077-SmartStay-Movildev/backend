using SmartStay.Bookings.Domain.Services;

namespace SmartStay.Bookings.Infrastructure.Workers;

/// <summary>
/// Servicio en segundo plano que revisa periódicamente reservas en estado PendingPayment cuyo TTL ha expirado,
/// liberando automáticamente sus cerrojos de noches (room_night_locks) y transicionando su estado a Expired.
/// </summary>
public class BookingExpirationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BookingExpirationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BookingExpirationWorker started with interval {Interval}.", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var commandService = scope.ServiceProvider.GetRequiredService<IBookingCommandService>();
                var expiredCount = await commandService.ExpirePendingBookingsAsync();

                if (expiredCount > 0)
                {
                    logger.LogInformation("Expired {Count} pending bookings and released night locks.", expiredCount);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while checking for expired bookings.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("BookingExpirationWorker stopped.");
    }
}
