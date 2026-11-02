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
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<BookingsPaymentConsumerWorker> logger) : BackgroundService
{
    private const string QueueName = "bookings.payment-completed.queue";
    private const string PaymentsTopicName = "payments.events";
    private const string BookingsTopicName = "bookings.events";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        logger.LogInformation("BookingsPaymentConsumerWorker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var brokerUri = configuration["ActiveMq:BrokerUri"] ?? "tcp://localhost:61616";
                var factory = new ConnectionFactory(brokerUri);

                using var connection = factory.CreateConnection();
                connection.Start();

                using var session = connection.CreateSession(AcknowledgementMode.ClientAcknowledge);
                var topic = session.GetTopic(PaymentsTopicName);
                var queue = session.GetQueue(QueueName);

                // Escuchar de la cola virtual o destino
                using var consumer = session.CreateConsumer(topic);

                logger.LogInformation("BookingsPaymentConsumerWorker connected to ActiveMQ broker at {Uri}.", brokerUri);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var message = consumer.Receive(TimeSpan.FromSeconds(2));
                    if (message is ITextMessage textMessage)
                    {
                        try
                        {
                            await ProcessPaymentEventAsync(textMessage.Text, stoppingToken);
                            textMessage.Acknowledge();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error processing payment event. Message not acknowledged for redelivery.");
                        }
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "ActiveMQ connection failed. Retrying in 5 seconds...");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task ProcessPaymentEventAsync(string messageJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(messageJson);
        var root = doc.RootElement;

        var eventType = root.GetProperty("eventType").GetString();
        var eventId = root.GetProperty("eventId").GetGuid();

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingsDbContext>();
        var bookingRepository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBookingsUnitOfWork>();

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
            var currency = dataElement.TryGetProperty("currency", out var currProp) ? currProp.GetString() ?? "USD" : "USD";

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
                    var isExpiredNow = booking.ExpiresAtUtc.HasValue && booking.ExpiresAtUtc.Value <= DateTime.UtcNow;
                    var isAmountMismatch = amount < booking.TotalAmount;
                    var isCurrencyMismatch = !string.Equals(currency, booking.Currency, StringComparison.OrdinalIgnoreCase);

                    if (isExpiredNow || isAmountMismatch || isCurrencyMismatch)
                    {
                        var reason = isExpiredNow
                            ? $"Booking #{bookingId} hold TTL expired at {booking.ExpiresAtUtc:O}."
                            : $"Payment amount/currency mismatch (Received: {amount} {currency}, Required: {booking.TotalAmount} {booking.Currency}).";

                        logger.LogWarning("Rejecting payment #{PaymentId} for Booking #{BookingId}: {Reason}", paymentId, bookingId, reason);

                        // Si ActiveMQ falla, lanza excepción, revierte tx y no ack para reintento seguro
                        PublishRefundRequestedOrThrow(paymentId, bookingId, reason);

                        inboxEntry.MarkProcessed();
                        await unitOfWork.CompleteAsync();
                        await tx.CommitAsync(ct);
                        return;
                    }

                    booking.Confirm();
                    inboxEntry.MarkProcessed();
                    await unitOfWork.CompleteAsync();
                    await tx.CommitAsync(ct);
                    logger.LogInformation("Booking #{BookingId} successfully confirmed via ActiveMQ Payment #{PaymentId}.", bookingId, paymentId);
                }
                else if (booking.Status == BookingStatus.Cancelled || booking.Status == BookingStatus.Expired)
                {
                    // No resucitar reservas vencidas o canceladas: emitir compensación de reembolso obligatoria
                    logger.LogWarning("Booking #{BookingId} in status {Status} received payment #{PaymentId}. Requesting refund compensation.", bookingId, booking.Status, paymentId);

                    PublishRefundRequestedOrThrow(paymentId, bookingId, $"Booking #{bookingId} was in status {booking.Status} when payment arrived.");

                    inboxEntry.MarkProcessed();
                    await unitOfWork.CompleteAsync();
                    await tx.CommitAsync(ct);
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

    private void PublishRefundRequestedOrThrow(int paymentId, int bookingId, string reason)
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
        logger.LogInformation("Successfully published refund request compensation event for Payment #{PaymentId}, Booking #{BookingId}.", paymentId, bookingId);
    }
}
