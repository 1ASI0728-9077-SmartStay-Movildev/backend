using System.Text.Json;
using Apache.NMS;
using Apache.NMS.ActiveMQ;
using SmartStay.Contracts.Events.V1;
using SmartStay.E2E.Tests.Infrastructure.Configuration;

namespace SmartStay.E2E.Tests.Infrastructure.Fixtures;

/// <summary>
/// Fixture providing ActiveMQ OpenWire connection management, event publishing, and message spying.
/// </summary>
public class ActiveMqFixture : IAsyncLifetime, IDisposable
{
    private IConnection? _connection;
    private ISession? _session;
    private readonly object _lock = new();

    public ActiveMqFixture()
    {
        try
        {
            PurgeTestQueues();
        }
        catch
        {
            // Best effort queue cleanup on fixture creation
        }
    }

    public IConnection Connection
    {
        get
        {
            EnsureConnected();
            return _connection!;
        }
    }

    public ISession Session
    {
        get
        {
            EnsureConnected();
            return _session!;
        }
    }

    public Task InitializeAsync()
    {
        EnsureConnected();
        try
        {
            PurgeTestQueues();
        }
        catch
        {
            // Best effort queue cleanup on initialization
        }
        return Task.CompletedTask;
    }

    public void PurgeQueue(string queueName)
    {
        try
        {
            using var handler = new HttpClientHandler();
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            var auth = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{E2ETestConfig.ActiveMqUser}:{E2ETestConfig.ActiveMqPassword}"));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
            client.DefaultRequestHeaders.Add("Origin", E2ETestConfig.ActiveMqWebConsoleUrl);
            var url = $"{E2ETestConfig.ActiveMqWebConsoleUrl}/api/jolokia/exec/org.apache.activemq:type=Broker,brokerName=localhost,destinationType=Queue,destinationName={Uri.EscapeDataString(queueName)}/purge";
            var response = client.GetAsync(url).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode) return;
        }
        catch
        {
            // Fallback to DrainQueue via OpenWire if HTTP Jolokia is unavailable
        }

        try
        {
            DrainQueue(queueName, TimeSpan.FromMilliseconds(50));
        }
        catch
        {
            // Best effort
        }
    }

    public void PurgeTestQueues()
    {
        PurgeQueue("Consumer.TestRunner.VirtualTopic.SmartStay.Payments.V1");
        PurgeQueue("Consumer.TestRunner.VirtualTopic.SmartStay.Bookings.V1");
    }

    public int DrainQueue(string queueName, TimeSpan? timeout = null)
    {
        EnsureConnected();
        var wait = timeout ?? TimeSpan.FromMilliseconds(500);
        using var session = _connection!.CreateSession(AcknowledgementMode.AutoAcknowledge);
        var queue = session.GetQueue(queueName);
        using var consumer = session.CreateConsumer(queue);
        int count = 0;
        while (true)
        {
            var msg = consumer.Receive(wait);
            if (msg == null) break;
            count++;
        }
        return count;
    }

    private void EnsureConnected()
    {
        lock (_lock)
        {
            if (_connection != null && _session != null) return;

            var factory = new ConnectionFactory(E2ETestConfig.ActiveMqBrokerUri);
            _connection = factory.CreateConnection(E2ETestConfig.ActiveMqUser, E2ETestConfig.ActiveMqPassword);
            _connection.Start();
            _session = _connection.CreateSession(AcknowledgementMode.AutoAcknowledge);
            PurgeTestQueues();
        }
    }

    public Task<bool> IsBrokerReachableAsync()
    {
        try
        {
            EnsureConnected();
            return Task.FromResult(_connection != null && _session != null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public void PublishToTopic(string topicName, string payload, IDictionary<string, string>? properties = null, MsgDeliveryMode deliveryMode = MsgDeliveryMode.Persistent)
    {
        EnsureConnected();
        var destination = _session!.GetTopic(topicName);
        using var producer = _session.CreateProducer(destination);
        producer.DeliveryMode = deliveryMode;

        var message = producer.CreateTextMessage(payload);
        if (properties != null)
        {
            foreach (var (key, value) in properties)
            {
                message.Properties[key] = value;
            }
        }

        producer.Send(message);
    }

    public void PublishToQueue(string queueName, string payload, IDictionary<string, string>? properties = null, MsgDeliveryMode deliveryMode = MsgDeliveryMode.Persistent)
    {
        EnsureConnected();
        var destination = _session!.GetQueue(queueName);
        using var producer = _session.CreateProducer(destination);
        producer.DeliveryMode = deliveryMode;

        var message = producer.CreateTextMessage(payload);
        if (properties != null)
        {
            foreach (var (key, value) in properties)
            {
                message.Properties[key] = value;
            }
        }

        producer.Send(message);
    }

    public void PublishIntegrationEvent<T>(string topicName, T data, string eventType, Guid? eventId = null, string? correlationId = null)
    {
        var id = eventId ?? Guid.NewGuid();
        var envelope = new IntegrationEvent<T>(
            EventId: id,
            EventType: eventType,
            SchemaVersion: 1,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: correlationId ?? Guid.NewGuid().ToString(),
            CausationId: null,
            AggregateId: id.ToString(),
            AggregateVersion: 1,
            Data: data
        );

        var payload = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var properties = new Dictionary<string, string>
        {
            ["EventType"] = eventType,
            ["EventId"] = id.ToString()
        };

        PublishToTopic(topicName, payload, properties);
    }

    public async Task<ITextMessage?> WaitForMessageAsync(string queueName, Func<ITextMessage, bool> predicate, TimeSpan timeout, string? messageSelector = null)
    {
        EnsureConnected();
        using var session = _connection!.CreateSession(AcknowledgementMode.IndividualAcknowledge);
        var destination = session.GetQueue(queueName);
        using var consumer = string.IsNullOrEmpty(messageSelector)
            ? session.CreateConsumer(destination)
            : session.CreateConsumer(destination, messageSelector);

        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            var waitTime = remaining > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining;
            var message = consumer.Receive(waitTime) as ITextMessage;

            if (message != null)
            {
                if (predicate(message))
                {
                    message.Acknowledge();
                    return message;
                }
                // DO NOT acknowledge unmatching messages!
                // Continue immediately to check if subsequent message in queue matches
                continue;
            }

            await Task.Delay(100);
        }

        return null;
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try
        {
            PurgeTestQueues();
        }
        catch { }

        lock (_lock)
        {
            _session?.Dispose();
            _session = null;
            if (_connection != null)
            {
                try { _connection.Close(); } catch { }
                _connection.Dispose();
                _connection = null;
            }
        }
        GC.SuppressFinalize(this);
    }
}
