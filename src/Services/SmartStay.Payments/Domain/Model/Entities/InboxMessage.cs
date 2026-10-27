namespace SmartStay.Payments.Domain.Model.Entities;

public class InboxMessage
{
    public InboxMessage() { }

    public InboxMessage(string consumerName, Guid eventId, string eventType, DateTime receivedAtUtc)
    {
        ConsumerName = consumerName;
        EventId = eventId;
        EventType = eventType;
        ReceivedAtUtc = receivedAtUtc;
        Status = "Received";
    }

    public string ConsumerName { get; private set; } = string.Empty;
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public string Status { get; private set; } = "Received";
    public string? LastError { get; private set; }

    public void MarkProcessed()
    {
        Status = "Processed";
        ProcessedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = "Failed";
        LastError = error;
    }
}
