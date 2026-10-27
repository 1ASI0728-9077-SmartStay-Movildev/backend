using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartStay.Contracts.Events.V1;
using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Domain.Model.Commands;
using SmartStay.Payments.Domain.Model.Entities;
using SmartStay.Payments.Domain.Repositories;
using SmartStay.Payments.Domain.Services;
using SmartStay.Payments.Infrastructure.Clients;

namespace SmartStay.Payments.Application.Internal.CommandServices;

public class PaymentCommandService(
    IPaymentRepository paymentRepository,
    IPaymentsUnitOfWork unitOfWork,
    IBookingEligibilityClient eligibilityClient,
    ILogger<PaymentCommandService> logger) : IPaymentCommandService
{
    public async Task<(Payment? Payment, bool IsDuplicate, string? ConflictReason)> Handle(ProcessPaymentCommand command)
    {
        // 1. Calcular hash de la solicitud para verificación de idempotencia
        var requestSignature = $"{command.BookingId}:{command.Amount:F2}:{command.PaymentMethod}:{command.CardHolderName}:{command.CardNumber}";
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestSignature)));

        // 2. Comprobar Idempotency-Key si fue proporcionada
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existingRecord = await paymentRepository.FindIdempotencyRecordAsync(command.IdempotencyKey);
            if (existingRecord is not null)
            {
                if (existingRecord.RequestHash == requestHash)
                {
                    logger.LogInformation("Idempotent replay detected for key {Key}.", command.IdempotencyKey);
                    var existingPayment = await paymentRepository.FindByBookingIdAsync(command.BookingId);
                    return (existingPayment, true, null);
                }
                else
                {
                    logger.LogWarning("Idempotency key {Key} reused with differing payload.", command.IdempotencyKey);
                    return (null, false, "Idempotency key already used for a different request payload.");
                }
            }
        }

        // 3. Regla de negocio: impedir cobros duplicados sobre la misma obligación de reserva
        if (await paymentRepository.HasCompletedPaymentForBookingAsync(command.BookingId))
        {
            logger.LogWarning("A completed payment already exists for booking #{BookingId}.", command.BookingId);
            return (null, false, $"A completed payment already exists for booking #{command.BookingId}.");
        }

        // 4. Consultar elegibilidad del agregado de reserva en Bookings
        var eligibility = await eligibilityClient.CheckEligibilityAsync(command.BookingId);
        if (eligibility is null || !eligibility.IsEligibleForPayment)
        {
            logger.LogWarning("Booking #{BookingId} is not eligible for payment. Status: {Status}", command.BookingId, eligibility?.Status ?? "NotFound");
            return (null, false, $"Booking #{command.BookingId} is not eligible for payment (Status: {eligibility?.Status ?? "NotFound"}).");
        }

        if (command.Amount != eligibility.TotalAmount)
        {
            logger.LogWarning("Payment amount {Amount} differs from booking total {Total}.", command.Amount, eligibility.TotalAmount);
            return (null, false, $"Payment amount {command.Amount} does not match booking total {eligibility.TotalAmount}.");
        }

        // 5. Instanciar agregado de Pago
        var payment = new Payment(
            command.BookingId,
            command.Amount,
            command.PaymentMethod,
            command.CardHolderName,
            command.CardNumber
        );

        // 6. Simulación de pasarela bancaria
        var isApproved = command.Amount > 0 && !command.CardNumber.EndsWith("0000");

        if (isApproved)
        {
            payment.Complete();
        }
        else
        {
            payment.Fail();
        }

        // 7. Transacción atómica en MySQL: Payment + OutboxMessage + IdempotencyRecord
        await using var tx = await unitOfWork.BeginTransactionAsync();
        try
        {
            await paymentRepository.AddAsync(payment);
            await unitOfWork.CompleteAsync(); // Genera payment.Id autoincremental

            var eventId = Guid.NewGuid();
            OutboxMessage outboxMessage;

            if (isApproved)
            {
                var evt = new IntegrationEvent<PaymentCompletedV1>(
                    EventId: eventId,
                    EventType: "payments.payment-completed",
                    SchemaVersion: 1,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    CorrelationId: command.IdempotencyKey ?? eventId.ToString(),
                    CausationId: null,
                    AggregateId: payment.Id.ToString(),
                    AggregateVersion: 1,
                    Data: new PaymentCompletedV1(payment.Id, payment.BookingId, eligibility.HotelId, payment.Amount, eligibility.Currency)
                );
                var payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                outboxMessage = new OutboxMessage(eventId, "payments.payment-completed", 1, payment.Id.ToString(), 1, payload);
            }
            else
            {
                var evt = new IntegrationEvent<PaymentFailedV1>(
                    EventId: eventId,
                    EventType: "payments.payment-failed",
                    SchemaVersion: 1,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    CorrelationId: command.IdempotencyKey ?? eventId.ToString(),
                    CausationId: null,
                    AggregateId: payment.Id.ToString(),
                    AggregateVersion: 1,
                    Data: new PaymentFailedV1(payment.Id, payment.BookingId, "Card declined by simulation logic.")
                );
                var payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                outboxMessage = new OutboxMessage(eventId, "payments.payment-failed", 1, payment.Id.ToString(), 1, payload);
            }

            await paymentRepository.AddOutboxMessageAsync(outboxMessage);

            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var idempotencyRecord = new PaymentIdempotencyRecord(command.IdempotencyKey, command.ActorId, requestHash);
                idempotencyRecord.MarkCompleted(payment.Id.ToString());
                await paymentRepository.AddIdempotencyRecordAsync(idempotencyRecord);
            }

            await unitOfWork.CompleteAsync();
            await tx.CommitAsync();

            logger.LogInformation("Payment #{PaymentId} processed with status {Status} and outbox event {EventId} stored.",
                payment.Id, payment.Status, eventId);

            return (payment, false, null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            logger.LogError(ex, "Failed to persist payment and outbox for booking #{BookingId}.", command.BookingId);
            throw;
        }
    }

    public async Task<bool> Handle(RefundPaymentCommand command)
    {
        var payment = await paymentRepository.FindByIdAsync(command.PaymentId);
        if (payment is null) return false;

        if (payment.Status == PaymentStatus.Refunded)
        {
            logger.LogInformation("Payment #{PaymentId} is already refunded.", payment.Id);
            return true;
        }

        await using var tx = await unitOfWork.BeginTransactionAsync();
        try
        {
            payment.Refund();
            paymentRepository.Update(payment);

            var eventId = Guid.NewGuid();
            var evt = new IntegrationEvent<PaymentRefundedV1>(
                EventId: eventId,
                EventType: "payments.payment-refunded",
                SchemaVersion: 1,
                OccurredAtUtc: DateTimeOffset.UtcNow,
                CorrelationId: eventId.ToString(),
                CausationId: null,
                AggregateId: payment.Id.ToString(),
                AggregateVersion: 2,
                Data: new PaymentRefundedV1(payment.Id, payment.BookingId, payment.Amount)
            );

            var payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var outboxMessage = new OutboxMessage(eventId, "payments.payment-refunded", 1, payment.Id.ToString(), 2, payload);

            await paymentRepository.AddOutboxMessageAsync(outboxMessage);
            await unitOfWork.CompleteAsync();
            await tx.CommitAsync();

            logger.LogInformation("Payment #{PaymentId} refunded successfully and outbox event queued.", payment.Id);
            return true;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            logger.LogError(ex, "Failed to refund payment #{PaymentId}.", payment.Id);
            throw;
        }
    }
}
