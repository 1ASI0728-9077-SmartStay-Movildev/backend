using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Interfaces.REST.Resources;

namespace SmartStay.Payments.Interfaces.REST.Transform;

public static class PaymentAssemblers
{
    public static PaymentResource ToResourceFromEntity(Payment entity)
    {
        var voucherSuffix = entity.TransactionId.Length >= 8
            ? entity.TransactionId[..8].ToUpperInvariant()
            : entity.TransactionId.ToUpperInvariant();

        return new PaymentResource(
            entity.Id,
            entity.BookingId,
            entity.TransactionId,
            entity.Amount,
            entity.Status.ToString(),
            entity.CardNumberMasked,
            entity.PaymentDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            entity.DemoPaymentMethodId,
            $"VCH-{entity.Id:D6}-{voucherSuffix}"
        );
    }

    public static DemoPaymentMethodResource ToResourceFromEntity(DemoPaymentMethod entity)
    {
        return new DemoPaymentMethodResource(
            entity.Id,
            entity.GuestUserId,
            entity.Label,
            entity.Status,
            entity.Balance,
            entity.Currency
        );
    }
}
