using System.Text.Json;
using Apache.NMS;
using Apache.NMS.ActiveMQ;
using Microsoft.EntityFrameworkCore;
using SmartStay.Analytics.Domain.Model.Entities;
using SmartStay.Analytics.Domain.Repositories;
using SmartStay.Analytics.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Analytics.Infrastructure.Messaging;

public class AnalyticsActiveMqConsumerWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AnalyticsActiveMqConsumerWorker> logger) : BackgroundService
{
    private const string PaymentsQueueName = "Consumer.Analytics.VirtualTopic.SmartStay.Payments.V1";
    private const string BookingsQueueName = "Consumer.Analytics.VirtualTopic.SmartStay.Bookings.V1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var brokerUri = configuration["ActiveMq:BrokerUri"] ?? "tcp://localhost:61616";
        logger.LogInformation("Starting AnalyticsActiveMqConsumerWorker connected to {BrokerUri}", brokerUri);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory(brokerUri);
                using var connection = factory.CreateConnection();
                connection.Start();

                using var session = connection.CreateSession(AcknowledgementMode.IndividualAcknowledge);
                var paymentsQueue = session.GetQueue(PaymentsQueueName);
                var bookingsQueue = session.GetQueue(BookingsQueueName);

                using var paymentsConsumer = session.CreateConsumer(paymentsQueue);
                using var bookingsConsumer = session.CreateConsumer(bookingsQueue);

                while (!stoppingToken.IsCancellationRequested)
                {
                    // Consumir eventos de pagos
                    var paymentMsg = paymentsConsumer.Receive(TimeSpan.FromMilliseconds(500));
                    if (paymentMsg is ITextMessage ptMsg)
                    {
                        await ProcessEventMessageAsync(ptMsg.Text, stoppingToken);
                        paymentMsg.Acknowledge();
                    }

                    // Consumir eventos de reservas
                    var bookingMsg = bookingsConsumer.Receive(TimeSpan.FromMilliseconds(500));
                    if (bookingMsg is ITextMessage bkMsg)
                    {
                        await ProcessEventMessageAsync(bkMsg.Text, stoppingToken);
                        bookingMsg.Acknowledge();
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "ActiveMQ consumer error in Analytics worker. Retrying in 5 seconds...");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    public async Task ProcessEventMessageAsync(string messageJson, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
        var analyticsRepository = scope.ServiceProvider.GetRequiredService<IAnalyticsRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IAnalyticsUnitOfWork>();

        using var doc = JsonDocument.Parse(messageJson);
        var root = doc.RootElement;
        var eventId = root.GetProperty("eventId").GetGuid();
        var eventType = root.GetProperty("eventType").GetString() ?? string.Empty;

        var alreadyProcessed = await dbContext.InboxMessages
            .AnyAsync(m => m.ConsumerName == "Analytics" && m.EventId == eventId, ct);

        if (alreadyProcessed)
        {
            logger.LogInformation("Inbox duplicate detected in Analytics for EventId {EventId}.", eventId);
            return;
        }

        await using var tx = await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var inboxEntry = new InboxMessage("Analytics", eventId, eventType, DateTime.UtcNow);
            await analyticsRepository.AddInboxMessageAsync(inboxEntry);

            var dataElement = root.GetProperty("data");

            if (eventType == "payments.payment-completed")
            {
                var paymentId = dataElement.GetProperty("paymentId").GetInt32();
                var bookingId = dataElement.GetProperty("bookingId").GetInt32();
                var hotelId = dataElement.GetProperty("hotelId").GetInt32();
                var amount = dataElement.GetProperty("amount").GetDecimal();
                var currency = dataElement.GetProperty("currency").GetString() ?? "USD";

                var fact = new RevenueFact(paymentId, bookingId, hotelId, amount, currency, DateTime.UtcNow);
                await analyticsRepository.UpsertRevenueFactAsync(fact);
            }
            else if (eventType == "payments.payment-refunded")
            {
                var paymentId = dataElement.GetProperty("paymentId").GetInt32();
                var amount = dataElement.GetProperty("amount").GetDecimal();

                var fact = new RevenueFact(paymentId, 0, 0, 0, "USD", DateTime.UtcNow);
                fact.MarkRefunded(amount);
                await analyticsRepository.UpsertRevenueFactAsync(fact);
            }
            else if (eventType == "bookings.booking-created")
            {
                var bookingId = dataElement.GetProperty("bookingId").GetInt32();
                var roomId = dataElement.GetProperty("roomId").GetInt32();
                var hotelId = dataElement.GetProperty("hotelId").GetInt32();
                var checkIn = dataElement.GetProperty("checkInDate").GetDateTime();
                var checkOut = dataElement.GetProperty("checkOutDate").GetDateTime();

                var fact = new BookingFact(bookingId, roomId, hotelId, "PendingPayment", checkIn, checkOut, DateTime.UtcNow);
                await analyticsRepository.UpsertBookingFactAsync(fact);
            }
            else if (eventType == "bookings.booking-confirmed")
            {
                var bookingId = dataElement.GetProperty("bookingId").GetInt32();
                var roomId = dataElement.GetProperty("roomId").GetInt32();
                var hotelId = dataElement.GetProperty("hotelId").GetInt32();

                var fact = new BookingFact(bookingId, roomId, hotelId, "Confirmed", DateTime.UtcNow, DateTime.UtcNow.AddDays(1), DateTime.UtcNow);
                await analyticsRepository.UpsertBookingFactAsync(fact);
            }
            else if (eventType == "bookings.booking-cancelled")
            {
                var bookingId = dataElement.GetProperty("bookingId").GetInt32();
                var roomId = dataElement.GetProperty("roomId").GetInt32();

                var fact = new BookingFact(bookingId, roomId, 0, "Cancelled", DateTime.UtcNow, DateTime.UtcNow.AddDays(1), DateTime.UtcNow);
                await analyticsRepository.UpsertBookingFactAsync(fact);
            }

            inboxEntry.MarkProcessed();
            await unitOfWork.CompleteAsync();
            await tx.CommitAsync(ct);

            logger.LogInformation("Projected event {EventType} (#{EventId}) into Analytics local facts.", eventType, eventId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            logger.LogError(ex, "Failed to project event {EventId} in Analytics worker.", eventId);
            throw;
        }
    }
}
