namespace SmartStay.Payments.Domain.Model.Entities;

public class PaymentIdempotencyRecord
{
    public PaymentIdempotencyRecord() { }

    public PaymentIdempotencyRecord(string idempotencyKey, int actorId, string requestHash)
    {
        IdempotencyKey = idempotencyKey;
        ActorId = actorId;
        RequestHash = requestHash;
        Status = "Processing";
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public int ActorId { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Processing";
    public string? ResponsePayload { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void MarkCompleted(string responsePayload)
    {
        Status = "Completed";
        ResponsePayload = responsePayload;
    }

    public void MarkFailed(string error)
    {
        Status = "Failed";
        ResponsePayload = error;
    }
}
