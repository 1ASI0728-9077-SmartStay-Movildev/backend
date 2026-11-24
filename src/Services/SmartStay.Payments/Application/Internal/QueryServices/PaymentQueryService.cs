using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Domain.Model.Queries;
using SmartStay.Payments.Domain.Repositories;
using SmartStay.Payments.Domain.Services;

namespace SmartStay.Payments.Application.Internal.QueryServices;

public class PaymentQueryService(IPaymentRepository paymentRepository) : IPaymentQueryService
{
    public async Task<Payment?> Handle(GetPaymentByBookingIdQuery query)
    {
        return await paymentRepository.FindByBookingIdAsync(query.BookingId);
    }

    public async Task<Payment?> Handle(GetPaymentByIdQuery query)
    {
        return await paymentRepository.FindByIdAsync(query.PaymentId);
    }

    public async Task<DemoPaymentMethod?> Handle(GetDemoPaymentMethodByGuestUserIdQuery query)
    {
        return await paymentRepository.FindDemoPaymentMethodByGuestUserIdAsync(query.GuestUserId);
    }

    public async Task<(bool IsValid, string? Reason, DemoPaymentMethod? Method)> Handle(ValidateDemoPaymentMethodQuery query)
    {
        var method = await paymentRepository.FindDemoPaymentMethodByIdAsync(query.MethodId);
        if (method is null)
        {
            return (false, "Payment method not found.", null);
        }

        if (method.GuestUserId != query.GuestUserId)
        {
            return (false, "Payment method is not owned by guest.", method);
        }

        if (!string.Equals(method.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Payment method is inactive.", method);
        }

        return (true, null, method);
    }
}
