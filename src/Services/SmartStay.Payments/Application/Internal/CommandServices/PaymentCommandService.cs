using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartStay.Contracts.Events.V1;
using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Domain.Model.Commands;
using SmartStay.Payments.Domain.Model.Entities;
using SmartStay.Payments.Domain.Repositories;
using SmartStay.Payments.Domain.Services;
using SmartStay.Payments.Infrastructure.Clients;
using SmartStay.Payments.Infrastructure.Configuration;

namespace SmartStay.Payments.Application.Internal.CommandServices;

public class PaymentCommandService(
    IPaymentRepository paymentRepository,
    IPaymentsUnitOfWork unitOfWork,
    IBookingEligibilityClient eligibilityClient,
    ILogger<PaymentCommandService> logger,
    IOptions<PaymentFeaturesOptions>? featuresOptions = null) : IPaymentCommandService
{
    public async Task<(Payment? Payment, bool IsDuplicate, string? ConflictReason)> Handle(ProcessPaymentCommand command)
    {
        // 1. Calcular hash de la solicitud para verificación de idempotencia
        var requestSignature = $"{command.BookingId}:{command.Amount:F2}:{command.PaymentMethod}:{command.CardHolderName}:{command.CardNumber}:{command.DemoPaymentMethodId}";
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestSignature)));

        // 2. Comprobar Idempotency-Key si fue proporcionada
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existingRecord = await paymentRepository.FindIdempotencyRecordAsync(command.ActorId, command.IdempotencyKey);
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

        var isDemoCard = string.Equals(command.PaymentMethod, "DemoCard", StringComparison.OrdinalIgnoreCase);

        // 5. Flujo específico para DemoCard
        if (isDemoCard)
        {
            var isDemoEnabled = featuresOptions?.Value.EnableDemoPayment ?? false;
            if (!isDemoEnabled)
            {
                logger.LogWarning("DemoCard payment rejected because demo payment feature is disabled.");
                return (null, false, "Demo payment feature is disabled.");
            }

            if (string.IsNullOrWhiteSpace(command.DemoPaymentMethodId))
            {
                logger.LogWarning("DemoCard payment rejected because DemoPaymentMethodId was not provided.");
                return (null, false, "Demo payment method ID is required for DemoCard payments.");
            }

            await using var tx = await unitOfWork.BeginTransactionAsync();
            try
            {
                var demoMethod = await paymentRepository.FindDemoPaymentMethodByIdForUpdateAsync(command.DemoPaymentMethodId);
                if (demoMethod is null)
                {
                    logger.LogWarning("Demo payment method {MethodId} not found.", command.DemoPaymentMethodId);
                    return (null, false, "Demo payment method not found.");
                }

                if (!string.Equals(demoMethod.Status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Demo payment method {MethodId} is not active (Status: {Status}).", command.DemoPaymentMethodId, demoMethod.Status);
                    return (null, false, "Demo payment method is inactive.");
                }

                if (demoMethod.GuestUserId != command.ActorId)
                {
                    logger.LogWarning("Demo payment method {MethodId} is not owned by guest #{GuestId}.", command.DemoPaymentMethodId, command.ActorId);
                    return (null, false, "Demo payment method is not owned by guest.");
                }

                if (demoMethod.Balance < command.Amount)
                {
                    logger.LogWarning("Insufficient balance on demo method {MethodId}: available {Balance}, required {Amount}.",
                        command.DemoPaymentMethodId, demoMethod.Balance, command.Amount);
                    return (null, false, $"Insufficient balance: available {demoMethod.Balance}, required {command.Amount}.");
                }

                // Débito atómico del saldo de la tarjeta ficticia
                demoMethod.Debit(command.Amount);
                paymentRepository.UpdateDemoPaymentMethod(demoMethod);

                // Crear agregado de Pago completado
                var payment = new Payment(
                    command.BookingId,
                    command.Amount,
                    command.PaymentMethod,
                    !string.IsNullOrWhiteSpace(command.CardHolderName) ? command.CardHolderName : "Demo Guest",
                    cardNumber: string.Empty,
                    demoPaymentMethodId: demoMethod.Id
                );
                payment.Complete();

                await paymentRepository.AddAsync(payment);
                await unitOfWork.CompleteAsync(); // Genera payment.Id autoincremental

                // Registrar movimiento único en el libro de pagos (Ledger)
                var movement = new PaymentLedgerMovement(
                    demoMethod.Id,
                    payment.Id,
                    "Debit",
                    command.Amount,
                    demoMethod.Balance
                );
                await paymentRepository.AddLedgerMovementAsync(movement);

                // Outbox message
                var eventId = Guid.NewGuid();
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
                var outboxMessage = new OutboxMessage(eventId, "payments.payment-completed", 1, payment.Id.ToString(), 1, payload);
                await paymentRepository.AddOutboxMessageAsync(outboxMessage);

                // Registro de idempotencia
                if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
                {
                    var idempotencyRecord = new PaymentIdempotencyRecord(command.IdempotencyKey, command.ActorId, requestHash);
                    idempotencyRecord.MarkCompleted(payment.Id.ToString());
                    await paymentRepository.AddIdempotencyRecordAsync(idempotencyRecord);
                }

                await unitOfWork.CompleteAsync();
                await tx.CommitAsync();

                logger.LogInformation("Demo payment #{PaymentId} completed for booking #{BookingId}. New balance: {Balance}.",
                    payment.Id, command.BookingId, demoMethod.Balance);

                return (payment, false, null);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                logger.LogError(ex, "Failed to persist demo payment and ledger for booking #{BookingId}.", command.BookingId);
                throw;
            }
        }

        // 6. Flujo estándar (tarjeta de crédito tradicional simulada)
        var standardPayment = new Payment(
            command.BookingId,
            command.Amount,
            command.PaymentMethod,
            command.CardHolderName,
            command.CardNumber
        );

        var isApproved = command.Amount > 0 && !command.CardNumber.EndsWith("0000");

        if (isApproved)
        {
            standardPayment.Complete();
        }
        else
        {
            standardPayment.Fail();
        }

        // 7. Transacción atómica en MySQL: Payment + OutboxMessage + IdempotencyRecord
        await using var standardTx = await unitOfWork.BeginTransactionAsync();
        try
        {
            await paymentRepository.AddAsync(standardPayment);
            await unitOfWork.CompleteAsync(); // Genera standardPayment.Id autoincremental

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
                    AggregateId: standardPayment.Id.ToString(),
                    AggregateVersion: 1,
                    Data: new PaymentCompletedV1(standardPayment.Id, standardPayment.BookingId, eligibility.HotelId, standardPayment.Amount, eligibility.Currency)
                );
                var payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                outboxMessage = new OutboxMessage(eventId, "payments.payment-completed", 1, standardPayment.Id.ToString(), 1, payload);
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
                    AggregateId: standardPayment.Id.ToString(),
                    AggregateVersion: 1,
                    Data: new PaymentFailedV1(standardPayment.Id, standardPayment.BookingId, "Card declined by simulation logic.")
                );
                var payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                outboxMessage = new OutboxMessage(eventId, "payments.payment-failed", 1, standardPayment.Id.ToString(), 1, payload);
            }

            await paymentRepository.AddOutboxMessageAsync(outboxMessage);

            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var idempotencyRecord = new PaymentIdempotencyRecord(command.IdempotencyKey, command.ActorId, requestHash);
                idempotencyRecord.MarkCompleted(standardPayment.Id.ToString());
                await paymentRepository.AddIdempotencyRecordAsync(idempotencyRecord);
            }

            await unitOfWork.CompleteAsync();
            await standardTx.CommitAsync();

            logger.LogInformation("Payment #{PaymentId} processed with status {Status} and outbox event {EventId} stored.",
                standardPayment.Id, standardPayment.Status, eventId);

            return (standardPayment, false, null);
        }
        catch (Exception ex)
        {
            await standardTx.RollbackAsync();
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

        var hasActiveTx = unitOfWork.HasActiveTransaction;
        IDbContextTransaction? tx = hasActiveTx ? null : await unitOfWork.BeginTransactionAsync();
        try
        {
            var isDemo = !string.IsNullOrEmpty(payment.DemoPaymentMethodId) ||
                         string.Equals(payment.PaymentMethod, "DemoCard", StringComparison.OrdinalIgnoreCase);

            if (isDemo && !string.IsNullOrEmpty(payment.DemoPaymentMethodId))
            {
                var existingRefundMovement = await paymentRepository.FindLedgerMovementAsync(payment.Id, "Refund");
                if (existingRefundMovement is null)
                {
                    var demoMethod = await paymentRepository.FindDemoPaymentMethodByIdForUpdateAsync(payment.DemoPaymentMethodId);
                    if (demoMethod is not null)
                    {
                        demoMethod.Credit(payment.Amount);
                        paymentRepository.UpdateDemoPaymentMethod(demoMethod);

                        var movement = new PaymentLedgerMovement(
                            demoMethod.Id,
                            payment.Id,
                            "Refund",
                            payment.Amount,
                            demoMethod.Balance
                        );
                        await paymentRepository.AddLedgerMovementAsync(movement);
                        logger.LogInformation("Restored {Amount} USD to demo payment method {MethodId}. New balance: {Balance}.",
                            payment.Amount, demoMethod.Id, demoMethod.Balance);
                    }
                }
            }

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

            if (tx is not null)
            {
                await tx.CommitAsync();
                await tx.DisposeAsync();
            }

            logger.LogInformation("Payment #{PaymentId} refunded successfully and outbox event queued.", payment.Id);
            return true;
        }
        catch (Exception ex)
        {
            if (tx is not null)
            {
                await tx.RollbackAsync();
                await tx.DisposeAsync();
            }
            logger.LogError(ex, "Failed to refund payment #{PaymentId}.", payment.Id);
            throw;
        }
    }

    public async Task<DemoPaymentMethod> Handle(ActivateDemoPaymentMethodCommand command)
    {
        var existing = await paymentRepository.FindDemoPaymentMethodByGuestUserIdAsync(command.GuestUserId);
        if (existing is not null)
        {
            logger.LogInformation("Returning existing demo payment method {MethodId} for guest #{GuestId}.", existing.Id, command.GuestUserId);
            return existing;
        }

        var method = new DemoPaymentMethod(command.GuestUserId);
        try
        {
            await paymentRepository.AddDemoPaymentMethodAsync(method);
            await unitOfWork.CompleteAsync();
            logger.LogInformation("Activated new demo payment method {MethodId} for guest #{GuestId}.", method.Id, command.GuestUserId);
            return method;
        }
        catch (DbUpdateException)
        {
            // Concurrency race: another thread activated the demo card concurrently
            var reloaded = await paymentRepository.FindDemoPaymentMethodByGuestUserIdAsync(command.GuestUserId);
            if (reloaded is not null) return reloaded;
            throw;
        }
    }
}
