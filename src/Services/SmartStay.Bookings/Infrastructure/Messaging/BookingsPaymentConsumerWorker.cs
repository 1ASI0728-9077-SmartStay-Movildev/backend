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
    private const string PaymentsQueueName = "Consumer.Bookings.VirtualTopic.SmartStay.Payments.V1";
    private const string BookingsTopicName = "VirtualTopic.SmartStay.Bookings.V1";

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
                var queue = session.GetQueue(PaymentsQueueName);
                using var consumer = session.CreateConsumer(queue);

                logger.LogInformation("BookingsPaymentConsumerWorker connected to ActiveMQ broker at {Uri} listening on {Queue}.", brokerUri, PaymentsQueueName);

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
                logger.LogWarning(ex, "ActiveMQ connection failed in BookingsPaymentConsumerWorker. Retrying in 5 seconds...");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    public async Task ProcessPaymentEventAsync(string messageJson, CancellationToken ct)
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
                inboxEntry.MarkProcessed();
                await dbContext.InboxMessages.AddAsync(inboxEntry, ct);

                var booking = await bookingRepository.FindByIdAsync(bookingId);
                if (booking is null)
                {
                    logger.LogWarning("Booking #{BookingId} not found when applying payment #{PaymentId}. Enqueueing refund compensation.", bookingId, paymentId);
                    await EnqueueRefundOutboxAsync(bookingRepository, paymentId, bookingId, $"Booking #{bookingId} not found.", eventId);
                    await unitOfWork.CompleteAsync();
                    await tx.CommitAsync(ct);
                    return;
                }

                if (booking.Status == BookingStatus.Confirmed)
                {
                    if (booking.PaymentId == paymentId)
                    {
                        logger.LogInformation("Booking #{BookingId} already confirmed with payment #{PaymentId}. Idempotent pass.", bookingId, paymentId);
                    }
                    else
                    {
                        logger.LogWarning("Booking #{BookingId} already confirmed with payment #{ExistingPaymentId}. Refunding payment #{PaymentId}.", bookingId, booking.PaymentId, paymentId);
                        await EnqueueRefundOutboxAsync(bookingRepository, paymentId, bookingId, $"Booking #{bookingId} already confirmed with another payment.", eventId);
                    }
                    await unitOfWork.CompleteAsync();
                    await tx.CommitAsync(ct);
                    return;
                }

                if (booking.Status == BookingStatus.Cancelled || booking.Status == BookingStatus.Expired)
                {
                    logger.LogWarning("Booking #{BookingId} in status {Status} received payment #{PaymentId}. Requesting refund compensation.", bookingId, booking.Status, paymentId);
                    await EnqueueRefundOutboxAsync(bookingRepository, paymentId, bookingId, $"Booking #{bookingId} was in status {booking.Status} when payment arrived.", eventId);
                    await unitOfWork.CompleteAsync();
                    await tx.CommitAsync(ct);
                    return;
                }

                if (booking.Status == BookingStatus.PendingPayment)
                {
                    var isExpiredNow = booking.ExpiresAtUtc.HasValue && booking.ExpiresAtUtc.Value <= DateTime.UtcNow;
                    var expectedNights = (booking.CheckOutDate.Date - booking.CheckInDate.Date).Days;
                    var hasValidLocks = booking.NightLocks != null && booking.NightLocks.Count == expectedNights;
                    var isAmountMismatch = amount != booking.TotalAmount;
                    var isCurrencyMismatch = !string.Equals(currency, booking.Currency, StringComparison.OrdinalIgnoreCase);

                    if (isExpiredNow || !hasValidLocks || isAmountMismatch || isCurrencyMismatch)
                    {
                        string reason;
                        if (isExpiredNow)
                        {
                            reason = $"Booking #{bookingId} hold TTL expired at {booking.ExpiresAtUtc:O}.";
                        }
                        else if (!hasValidLocks)
                        {
                            reason = $"Booking #{bookingId} room night locks invalid or released.";
                        }
                        else
                        {
                            reason = $"Payment amount/currency mismatch (Received: {amount} {currency}, Expected: {booking.TotalAmount} {booking.Currency}).";
                        }

                        logger.LogWarning("Rejecting payment #{PaymentId} for Booking #{BookingId}: {Reason}", paymentId, bookingId, reason);

                        if (isExpiredNow || !hasValidLocks)
                        {
                            booking.Expire();
                            await bookingRepository.RemoveLocksByBookingIdAsync(booking.Id);
                            bookingRepository.Update(booking);
                        }

                        await EnqueueRefundOutboxAsync(bookingRepository, paymentId, bookingId, reason, eventId);
                        await unitOfWork.CompleteAsync();
                        await tx.CommitAsync(ct);
                        return;
                    }

                    booking.Confirm(paymentId);
                    bookingRepository.Update(booking);
                    await unitOfWork.CompleteAsync();
                    await tx.CommitAsync(ct);
                    logger.LogInformation("Booking #{BookingId} successfully confirmed via ActiveMQ Payment #{PaymentId}.", bookingId, paymentId);
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

    private static async Task EnqueueRefundOutboxAsync(
        IBookingRepository bookingRepository,
        int paymentId,
        int bookingId,
        string reason,
        Guid causationEventId)
    {
        var eventId = Guid.NewGuid();
        var refundEvt = new IntegrationEvent<PaymentRefundRequestedV1>(
            EventId: eventId,
            EventType: "bookings.payment-refund-requested",
            SchemaVersion: 1,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: causationEventId.ToString(),
            CausationId: causationEventId.ToString(),
            AggregateId: bookingId.ToString(),
            AggregateVersion: 1,
            Data: new PaymentRefundRequestedV1(paymentId, bookingId, reason)
        );

        var payload = JsonSerializer.Serialize(refundEvt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var outboxMessage = new OutboxMessage(eventId, "bookings.payment-refund-requested", 1, bookingId.ToString(), 1, payload);
        await bookingRepository.AddOutboxMessageAsync(outboxMessage);
    }
}
