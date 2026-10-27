using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Interfaces.REST.Resources;

namespace SmartStay.Payments.Interfaces.REST.Transform;

public static class PaymentAssemblers
{
    public static PaymentResource ToResourceFromEntity(Payment entity)
    {
        return new PaymentResource(
            entity.Id,
            entity.BookingId,
            entity.TransactionId,
            entity.Amount,
            entity.Status.ToString(),
            entity.CardNumberMasked,
            entity.PaymentDate.ToString("yyyy-MM-ddTHH:mm:ssZ")
        );
    }
}
