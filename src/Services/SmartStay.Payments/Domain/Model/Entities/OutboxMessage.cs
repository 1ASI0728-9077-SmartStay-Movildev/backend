namespace SmartStay.Payments.Domain.Model.Entities;

public class OutboxMessage
{
    public OutboxMessage() { }

    public OutboxMessage(
        Guid id,
        string eventType,
        int schemaVersion,
        string aggregateId,
        long aggregateVersion,
        string payload)
    {
        Id = id;
        EventType = eventType;
        SchemaVersion = schemaVersion;
        AggregateId = aggregateId;
        AggregateVersion = aggregateVersion;
        Payload = payload;
        OccurredAtUtc = DateTime.UtcNow;
        Attempts = 0;
    }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; } = 1;
    public string AggregateId { get; private set; } = string.Empty;
    public long AggregateVersion { get; private set; } = 1;
    public string Payload { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public string? LastError { get; private set; }

    public void MarkPublished()
    {
        PublishedAtUtc = DateTime.UtcNow;
        LeaseOwner = null;
        LeaseUntilUtc = null;
    }

    public void RecordFailure(string error)
    {
        Attempts++;
        LastError = error;
        NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, Attempts)));
        LeaseOwner = null;
        LeaseUntilUtc = null;
    }
}
