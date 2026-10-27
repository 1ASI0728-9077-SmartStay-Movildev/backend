using System.Text.Json;
using Apache.NMS;
using Apache.NMS.ActiveMQ;
using Microsoft.EntityFrameworkCore;
using SmartStay.Payments.Domain.Model.Commands;
using SmartStay.Payments.Domain.Model.Entities;
using SmartStay.Payments.Domain.Repositories;
using SmartStay.Payments.Domain.Services;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Payments.Infrastructure.Messaging;

public class PaymentsCompensationConsumerWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PaymentsCompensationConsumerWorker> logger) : BackgroundService
{
    private const string CompensationQueueName = "Consumer.Payments.VirtualTopic.SmartStay.Bookings.V1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var brokerUri = configuration["ActiveMq:BrokerUri"] ?? "tcp://localhost:61616";
        logger.LogInformation("Starting PaymentsCompensationConsumerWorker connected to {BrokerUri} queue {Queue}", brokerUri, CompensationQueueName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory(brokerUri);
                using var connection = factory.CreateConnection();
                connection.Start();

                using var session = connection.CreateSession(AcknowledgementMode.IndividualAcknowledge);
                var queue = session.GetQueue(CompensationQueueName);
                using var consumer = session.CreateConsumer(queue);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var message = consumer.Receive(TimeSpan.FromSeconds(2));
                    if (message is null) continue;

                    if (message is ITextMessage textMessage)
                    {
                        await ProcessCompensationEventAsync(textMessage.Text, stoppingToken);
                        message.Acknowledge();
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "ActiveMQ compensation consumer error in Payments. Retrying in 5s...");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    public async Task ProcessCompensationEventAsync(string messageJson, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var paymentCommandService = scope.ServiceProvider.GetRequiredService<IPaymentCommandService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>();

        using var doc = JsonDocument.Parse(messageJson);
        var root = doc.RootElement;
        var eventId = root.GetProperty("eventId").GetGuid();
        var eventType = root.GetProperty("eventType").GetString() ?? string.Empty;

        var alreadyProcessed = await dbContext.InboxMessages
            .AnyAsync(m => m.ConsumerName == "Payments" && m.EventId == eventId, ct);

        if (alreadyProcessed)
        {
            logger.LogInformation("Inbox duplicate detected for Compensation EventId {EventId}.", eventId);
            return;
        }

        if (eventType == "bookings.payment-refund-requested")
        {
            var dataElement = root.GetProperty("data");
            var paymentId = dataElement.GetProperty("paymentId").GetInt32();
            var reason = dataElement.GetProperty("reason").GetString() ?? "Refund requested by Bookings.";

            var inboxEntry = new InboxMessage("Payments", eventId, eventType, DateTime.UtcNow);
            await dbContext.InboxMessages.AddAsync(inboxEntry, ct);
            await unitOfWork.CompleteAsync();

            await paymentCommandService.Handle(new RefundPaymentCommand(paymentId, reason));

            inboxEntry.MarkProcessed();
            await unitOfWork.CompleteAsync();

            logger.LogInformation("Payment #{PaymentId} compensation successfully executed for event {EventId}.", paymentId, eventId);
        }
    }
}
