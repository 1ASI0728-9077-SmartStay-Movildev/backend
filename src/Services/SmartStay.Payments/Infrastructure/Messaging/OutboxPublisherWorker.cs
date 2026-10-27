using Apache.NMS;
using Apache.NMS.ActiveMQ;
using SmartStay.Payments.Domain.Repositories;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Payments.Infrastructure.Messaging;

public class OutboxPublisherWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
    private const string PaymentsTopicName = "VirtualTopic.SmartStay.Payments.V1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var brokerUri = configuration["ActiveMq:BrokerUri"] ?? "tcp://localhost:61616";
        logger.LogInformation("OutboxPublisherWorker starting connected to {BrokerUri} publishing on {Topic}", brokerUri, PaymentsTopicName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var paymentRepository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>();

                var pendingMessages = await paymentRepository.GetPendingOutboxMessagesAsync(20, stoppingToken);

                if (pendingMessages.Count > 0)
                {
                    var factory = new ConnectionFactory(brokerUri);
                    using var connection = factory.CreateConnection();
                    connection.Start();

                    using var session = connection.CreateSession();
                    var destination = session.GetTopic(PaymentsTopicName);
                    using var producer = session.CreateProducer(destination);
                    producer.DeliveryMode = MsgDeliveryMode.Persistent;

                    foreach (var msg in pendingMessages)
                    {
                        try
                        {
                            var textMessage = producer.CreateTextMessage(msg.Payload);
                            textMessage.Properties["EventType"] = msg.EventType;
                            textMessage.Properties["EventId"] = msg.Id.ToString();

                            producer.Send(textMessage);
                            msg.MarkPublished();
                            logger.LogInformation("Outbox event {EventId} of type {EventType} published to ActiveMQ.", msg.Id, msg.EventType);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to publish outbox event {EventId} to ActiveMQ.", msg.Id);
                            msg.RecordFailure(ex.Message);
                        }
                    }

                    await unitOfWork.CompleteAsync();
                }

                await Task.Delay(1000, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Error in OutboxPublisherWorker polling loop. Backing off 3 seconds...");
                await Task.Delay(3000, stoppingToken);
            }
        }
    }
}
