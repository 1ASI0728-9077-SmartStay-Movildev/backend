namespace SmartStay.Payments.Domain.Model.Queries;

public record GetPaymentByBookingIdQuery(int BookingId);

public record GetPaymentByIdQuery(int PaymentId);
