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
}
