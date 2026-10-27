using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Domain.Model.Commands;
using SmartStay.Payments.Domain.Model.Queries;

namespace SmartStay.Payments.Domain.Services;

public interface IPaymentCommandService
{
    Task<(Payment? Payment, bool IsDuplicate, string? ConflictReason)> Handle(ProcessPaymentCommand command);
    Task<bool> Handle(RefundPaymentCommand command);
}

public interface IPaymentQueryService
{
    Task<Payment?> Handle(GetPaymentByBookingIdQuery query);
    Task<Payment?> Handle(GetPaymentByIdQuery query);
}
