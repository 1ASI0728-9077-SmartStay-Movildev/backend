using System.Text.Json;
using Apache.NMS;
using Apache.NMS.ActiveMQ;
using Microsoft.EntityFrameworkCore;
using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Entities;
using SmartStay.Bookings.Domain.Repositories;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Contracts.Events.V1;

namespace SmartStay.Bookings.Infrastructure.Messaging;

public class BookingsPaymentConsumerWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BookingsPaymentConsumerWorker> logger) : BackgroundService
{
    private const string ConsumerQueueName = "Consumer.Bookings.VirtualTopic.SmartStay.Payments.V1";
    private const string BookingsTopicName = "VirtualTopic.SmartStay.Bookings.V1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var brokerUri = configuration["ActiveMq:BrokerUri"] ?? "tcp://localhost:61616";
        logger.LogInformation("Starting BookingsPaymentConsumerWorker connected to {BrokerUri} listening on {Queue}", brokerUri, ConsumerQueueName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory(brokerUri);
                using var connection = factory.CreateConnection();
                connection.Start();

                using var session = connection.CreateSession(AcknowledgementMode.IndividualAcknowledge);
                var queue = session.GetQueue(ConsumerQueueName);
                using var consumer = session.CreateConsumer(queue);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var message = consumer.Receive(TimeSpan.FromSeconds(2));
                    if (message is null) continue;

                    if (message is ITextMessage textMessage)
                    {
                        await ProcessPaymentEventAsync(textMessage.Text, stoppingToken);
                        message.Acknowledge();
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "ActiveMQ consumer connection error in Bookings. Retrying in 5 seconds...");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    public async Task ProcessPaymentEventAsync(string messageJson, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingsDbContext>();
        var bookingRepository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBookingsUnitOfWork>();

        using var doc = JsonDocument.Parse(messageJson);
        var root = doc.RootElement;
        var eventId = root.GetProperty("eventId").GetGuid();
        var eventType = root.GetProperty("eventType").GetString() ?? string.Empty;

        // 1. Verificación idempotente en Inbox bajo clave única (ConsumerName, EventId)
        var alreadyProcessed = await dbContext.InboxMessages
            .AnyAsync(m => m.ConsumerName == "Bookings" && m.EventId == eventId, ct);

        if (alreadyProcessed)
        {
            logger.LogInformation("Inbox duplicate detected for EventId {EventId}. Skipping processing.", eventId);
            return;
        }

        if (eventType == "payments.payment-completed")
        {
            var dataElement = root.GetProperty("data");
            var bookingId = dataElement.GetProperty("bookingId").GetInt32();
            var paymentId = dataElement.GetProperty("paymentId").GetInt32();
            var amount = dataElement.GetProperty("amount").GetDecimal();

            await using var tx = await unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var inboxEntry = new InboxMessage("Bookings", eventId, eventType, DateTime.UtcNow);
                await dbContext.InboxMessages.AddAsync(inboxEntry, ct);

                var booking = await bookingRepository.FindByIdAsync(bookingId);
                if (booking is null)
                {
                    logger.LogWarning("Booking #{BookingId} not found when applying payment #{PaymentId}.", bookingId, paymentId);
                    inboxEntry.MarkFailed($"Booking #{bookingId} not found.");
                    await unitOfWork.CompleteAsync();
                    await tx.CommitAsync(ct);
                    return;
                }

                if (booking.Status == BookingStatus.PendingPayment)
                {
                    booking.Confirm();
                    inboxEntry.MarkProcessed();
                    await unitOfWork.CompleteAsync();
                    await tx.CommitAsync(ct);
                    logger.LogInformation("Booking #{BookingId} successfully confirmed via ActiveMQ Payment #{PaymentId}.", bookingId, paymentId);
                }
                else if (booking.Status == BookingStatus.Cancelled || booking.Status == BookingStatus.Expired)
                {
                    // No resucitar reservas vencidas o canceladas: emitir compensación de reembolso
                    logger.LogWarning("Booking #{BookingId} in status {Status} received payment #{PaymentId}. Requesting refund compensation.", bookingId, booking.Status, paymentId);
                    
                    inboxEntry.MarkProcessed();
                    await unitOfWork.CompleteAsync();
                    await tx.CommitAsync(ct);

                    PublishRefundRequested(paymentId, bookingId, $"Booking #{bookingId} was in status {booking.Status} when payment arrived.");
                }
                else
                {
                    // Ya confirmada previamente: idempotente
                    inboxEntry.MarkProcessed();
                    await unitOfWork.CompleteAsync();
                    await tx.CommitAsync(ct);
                    logger.LogInformation("Booking #{BookingId} was already in status {Status}. Payment #{PaymentId} marked as duplicate.", bookingId, booking.Status, paymentId);
                }
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "Failed to process payment event {EventId} for booking #{BookingId}.", eventId, bookingId);
                throw;
            }
        }
    }

    private void PublishRefundRequested(int paymentId, int bookingId, string reason)
    {
        try
        {
            var brokerUri = configuration["ActiveMq:BrokerUri"] ?? "tcp://localhost:61616";
            var factory = new ConnectionFactory(brokerUri);
            using var connection = factory.CreateConnection();
            connection.Start();

            using var session = connection.CreateSession();
            var destination = session.GetTopic(BookingsTopicName);
            using var producer = session.CreateProducer(destination);
            producer.DeliveryMode = MsgDeliveryMode.Persistent;

            var evt = new IntegrationEvent<PaymentRefundRequestedV1>(
                EventId: Guid.NewGuid(),
                EventType: "bookings.payment-refund-requested",
                SchemaVersion: 1,
                OccurredAtUtc: DateTimeOffset.UtcNow,
                CorrelationId: Guid.NewGuid().ToString(),
                CausationId: null,
                AggregateId: bookingId.ToString(),
                AggregateVersion: 1,
                Data: new PaymentRefundRequestedV1(paymentId, bookingId, reason)
            );

            var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var msg = producer.CreateTextMessage(json);
            producer.Send(msg);
            logger.LogInformation("Published refund request compensation event for Payment #{PaymentId}, Booking #{BookingId}.", paymentId, bookingId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish refund compensation event for Payment #{PaymentId}.", paymentId);
        }
    }
}
