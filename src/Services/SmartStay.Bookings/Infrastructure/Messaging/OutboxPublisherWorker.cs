using Apache.NMS;
using Apache.NMS.ActiveMQ;
using SmartStay.Bookings.Domain.Repositories;

namespace SmartStay.Bookings.Infrastructure.Messaging;

public class OutboxPublisherWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
    private const string BookingsTopicName = "VirtualTopic.SmartStay.Bookings.V1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var brokerUri = configuration["ActiveMq:BrokerUri"] ?? "tcp://localhost:61616";
        logger.LogInformation("OutboxPublisherWorker in Bookings starting connected to {BrokerUri} publishing on {Topic}", brokerUri, BookingsTopicName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var bookingRepository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IBookingsUnitOfWork>();

                var pendingMessages = await bookingRepository.GetPendingOutboxMessagesAsync(20, stoppingToken);

                if (pendingMessages.Count > 0)
                {
                    var factory = new ConnectionFactory(brokerUri);
                    using var connection = factory.CreateConnection();
                    connection.Start();

                    using var session = connection.CreateSession();
                    var destination = session.GetTopic(BookingsTopicName);
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
                            logger.LogInformation("Bookings Outbox event {EventId} of type {EventType} published to ActiveMQ.", msg.Id, msg.EventType);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to publish Bookings outbox event {EventId} to ActiveMQ.", msg.Id);
                            msg.RecordFailure(ex.Message);
                        }
                    }

                    await unitOfWork.CompleteAsync();
                }

                await Task.Delay(1000, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Error in Bookings OutboxPublisherWorker polling loop. Backing off 3 seconds...");
                await Task.Delay(3000, stoppingToken);
            }
        }
    }
}
