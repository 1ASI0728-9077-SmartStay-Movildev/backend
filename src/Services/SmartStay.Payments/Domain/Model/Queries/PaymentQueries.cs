namespace SmartStay.Payments.Domain.Model.Queries;

public record GetPaymentByBookingIdQuery(int BookingId);

public record GetPaymentByIdQuery(int PaymentId);

public record GetDemoPaymentMethodByGuestUserIdQuery(int GuestUserId);

public record GetDemoPaymentMethodByIdQuery(string MethodId);

public record ValidateDemoPaymentMethodQuery(string MethodId, int GuestUserId);
