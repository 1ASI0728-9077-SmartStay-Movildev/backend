using Microsoft.EntityFrameworkCore;
using SmartStay.Concierge.Application;
using SmartStay.Concierge.Domain;
using SmartStay.Concierge.Infrastructure.Clients;
using SmartStay.Concierge.Infrastructure.Persistence;

namespace SmartStay.Concierge.Infrastructure.Workers;

public class ConciergePurchaseIntentWorker(
    IServiceScopeFactory scopeFactory,
    IBookingsClient bookingsClient,
    IPaymentsClient paymentsClient,
    ICandidateEvaluator candidateEvaluator,
    ILogger<ConciergePurchaseIntentWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ConciergePurchaseIntentWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ConciergeDbContext>();

                var now = DateTime.UtcNow;
                var pendingIntents = await dbContext.PurchaseIntents
                    .Where(p => (p.Status == PurchaseIntentStatus.Pending ||
                                 p.Status == PurchaseIntentStatus.Reserving ||
                                 p.Status == PurchaseIntentStatus.AwaitingConfirmation ||
                                 p.Status == PurchaseIntentStatus.Cancelling)
                                && (p.NextRetryAtUtc == null || p.NextRetryAtUtc <= now))
                    .OrderBy(p => p.UpdatedAtUtc)
                    .Take(10)
                    .ToListAsync(stoppingToken);

                foreach (var intent in pendingIntents)
                {
                    try
                    {
                        await ProcessIntentAsync(dbContext, intent, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Unexpected error processing intent {IntentId}.", intent.Id);
                        ScheduleTransientRetry(intent);
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error in ConciergePurchaseIntentWorker polling cycle.");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    internal async Task ProcessIntentAsync(ConciergeDbContext db, PurchaseIntent intent, CancellationToken ct)
    {
        // 1. Mandate Expiration Check (15-minute TTL)
        if (intent.IsMandateExpired && intent.Status != PurchaseIntentStatus.Cancelling && intent.Status != PurchaseIntentStatus.Cancelled)
        {
            logger.LogWarning("Autonomous mandate expired (15m limit) for intent {IntentId}.", intent.Id);
            if (intent.BookingId.HasValue)
            {
                await bookingsClient.CancelBookingAsync(intent.BookingId.Value, "Mandate expired after 15 minutes", intent.GuestUserId, ct);
            }

            intent.TransitionToExpired();
            await db.SaveChangesAsync(ct);
            return;
        }

        switch (intent.Status)
        {
            case PurchaseIntentStatus.Pending:
                await HandlePendingAsync(db, intent, ct);
                break;

            case PurchaseIntentStatus.Reserving:
                await HandleReservingAsync(db, intent, ct);
                break;

            case PurchaseIntentStatus.AwaitingConfirmation:
                await HandleAwaitingConfirmationAsync(db, intent, ct);
                break;

            case PurchaseIntentStatus.Cancelling:
                await HandleCancellingAsync(db, intent, ct);
                break;
        }
    }

    private async Task HandlePendingAsync(ConciergeDbContext db, PurchaseIntent intent, CancellationToken ct)
    {
        var evalResult = await candidateEvaluator.EvaluateCandidatesAsync(intent, ct);

        if (evalResult.ImmediateBudgetFailure)
        {
            intent.TransitionToFailed("No available rooms within the specified budget.");
            await db.SaveChangesAsync(ct);
            return;
        }

        if (!evalResult.Success || evalResult.SelectedCandidate == null || evalResult.Quote == null)
        {
            intent.CandidateAttempts = 3;
            intent.TransitionToFailed(evalResult.FailureReason ?? "All eligible room candidates (up to 3) are unavailable or quotes expired.");
            await db.SaveChangesAsync(ct);
            return;
        }

        var candidate = evalResult.SelectedCandidate;
        var quote = evalResult.Quote;

        intent.TransitionToReserving(
            quoteId: quote.QuoteId,
            quoteExpiresAtUtc: quote.ExpiresAtUtc,
            roomId: candidate.RoomId,
            roomTypeName: candidate.RoomTypeName,
            hotelName: candidate.HotelName,
            stayTotal: quote.StayTotal
        );

        await db.SaveChangesAsync(ct);
    }

    private async Task HandleReservingAsync(ConciergeDbContext db, PurchaseIntent intent, CancellationToken ct)
    {
        // CRASH RECOVERY & QUERY-BEFORE-RETRY: Check if booking was already created
        var existingBooking = await bookingsClient.GetBookingByIntentIdAsync(intent.Id, ct);
        if (existingBooking != null)
        {
            logger.LogInformation("Recovered existing booking #{BookingId} for intent {IntentId}.", existingBooking.BookingId, intent.Id);
            intent.TransitionToAwaitingConfirmation(existingBooking.BookingId);
            await db.SaveChangesAsync(ct);
            return;
        }

        // Check if 2-minute quote expired before reservation
        if (intent.QuoteExpiresAtUtc.HasValue && DateTime.UtcNow > intent.QuoteExpiresAtUtc.Value)
        {
            logger.LogWarning("Quote {QuoteId} expired for intent {IntentId}.", intent.QuoteId, intent.Id);
            intent.CandidateAttempts++;
            if (intent.CandidateAttempts < 3)
            {
                intent.Status = PurchaseIntentStatus.Pending;
                intent.QuoteId = null;
                intent.Touch();
            }
            else
            {
                intent.TransitionToFailed("Evaluated up to 3 candidates; all quotes expired.");
            }

            await db.SaveChangesAsync(ct);
            return;
        }

        if (!intent.SelectedRoomId.HasValue || string.IsNullOrWhiteSpace(intent.QuoteId))
        {
            intent.Status = PurchaseIntentStatus.Pending;
            intent.Touch();
            await db.SaveChangesAsync(ct);
            return;
        }

        // Issue reservation with stable key: concierge-booking-{intentId}
        var reserveResult = await bookingsClient.CreateBookingAsync(
            roomId: intent.SelectedRoomId.Value,
            guestName: intent.ContactName,
            guestEmail: intent.ContactEmail,
            checkIn: intent.CheckIn.ToDateTime(TimeOnly.MinValue),
            checkOut: intent.CheckOut.ToDateTime(TimeOnly.MinValue),
            quoteId: intent.QuoteId,
            purchaseIntentId: intent.Id,
            guestUserId: intent.GuestUserId,
            idempotencyKey: intent.StableBookingIdempotencyKey,
            ct: ct);

        if (reserveResult.Success && reserveResult.BookingId.HasValue)
        {
            intent.TransitionToAwaitingConfirmation(reserveResult.BookingId.Value);
        }
        else if (reserveResult.IsTransientError)
        {
            ScheduleTransientRetry(intent);
        }
        else
        {
            // Non-transient failure (e.g. 409 conflict): fall back to next candidate
            intent.CandidateAttempts++;
            if (intent.CandidateAttempts < 3)
            {
                intent.Status = PurchaseIntentStatus.Pending;
                intent.QuoteId = null;
                intent.Touch();
            }
            else
            {
                intent.TransitionToFailed(reserveResult.ErrorMessage ?? "Could not book room after 3 candidate attempts.");
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task HandleAwaitingConfirmationAsync(ConciergeDbContext db, PurchaseIntent intent, CancellationToken ct)
    {
        if (!intent.BookingId.HasValue)
        {
            intent.Status = PurchaseIntentStatus.Reserving;
            intent.Touch();
            await db.SaveChangesAsync(ct);
            return;
        }

        // CRASH RECOVERY & QUERY-BEFORE-RETRY: Check if payment already exists
        var existingPayment = await paymentsClient.GetPaymentByBookingIdAsync(intent.BookingId.Value, ct);
        if (existingPayment != null)
        {
            if (existingPayment.Status == "Completed")
            {
                logger.LogInformation("Recovered existing completed payment #{PaymentId} for intent {IntentId}.", existingPayment.Id, intent.Id);
                intent.TransitionToConfirmed(existingPayment.Id, existingPayment.ReceiptVoucher ?? $"VCH-{existingPayment.Id:D6}");
                await db.SaveChangesAsync(ct);
                return;
            }

            if (existingPayment.Status == "Failed")
            {
                logger.LogWarning("Existing payment failed for booking #{BookingId}. Releasing night locks.", intent.BookingId.Value);
                await bookingsClient.CancelBookingAsync(intent.BookingId.Value, "Payment failed", intent.GuestUserId, ct);
                intent.TransitionToFailed("Simulator payment rejected or insufficient balance.");
                await db.SaveChangesAsync(ct);
                return;
            }
        }

        // Issue payment with stable key: concierge-payment-{intentId}
        var amount = intent.StayTotal ?? intent.BudgetTotal;
        var payResult = await paymentsClient.ProcessDemoPaymentAsync(
            bookingId: intent.BookingId.Value,
            amount: amount,
            currency: intent.Currency,
            demoPaymentMethodId: intent.DemoPaymentMethodId,
            guestUserId: intent.GuestUserId,
            idempotencyKey: intent.StablePaymentIdempotencyKey,
            ct: ct);

        if (payResult.Success && payResult.PaymentId.HasValue)
        {
            intent.TransitionToConfirmed(payResult.PaymentId.Value, payResult.ReceiptVoucher ?? $"VCH-{payResult.PaymentId.Value:D6}");
        }
        else if (payResult.IsTransientError)
        {
            ScheduleTransientRetry(intent);
        }
        else
        {
            // Payment rejected (simulator rejection or insufficient balance): release night locks
            logger.LogWarning("Payment rejected for booking #{BookingId}: {Reason}. Cancelling booking to release locks.",
                intent.BookingId.Value, payResult.ErrorMessage);
            await bookingsClient.CancelBookingAsync(intent.BookingId.Value, "Payment authorization rejected", intent.GuestUserId, ct);
            intent.TransitionToFailed(payResult.ErrorMessage ?? "Payment authorization rejected by simulator.");
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task HandleCancellingAsync(ConciergeDbContext db, PurchaseIntent intent, CancellationToken ct)
    {
        // 1. Si no hay reserva creada, cancelar inmediatamente
        if (!intent.BookingId.HasValue)
        {
            intent.TransitionToCancelled();
            await db.SaveChangesAsync(ct);
            return;
        }

        // 2. Si no hay pago procesado, cancelar reserva en Bookings y completar
        if (!intent.PaymentId.HasValue)
        {
            await bookingsClient.CancelBookingAsync(intent.BookingId.Value, "Cancelled by guest before payment", intent.GuestUserId, ct);
            intent.TransitionToCancelled();
            await db.SaveChangesAsync(ct);
            return;
        }

        // 3. Cancelación pagada: Asegurar llamada de cancelación a Bookings (emite reembolso al Outbox)
        await bookingsClient.CancelBookingAsync(intent.BookingId.Value, "Cancelled by guest (full refund demo policy)", intent.GuestUserId, ct);

        // Consultar Payments para comprobar si el reembolso fue aplicado (payment.Status == Refunded)
        var payment = await paymentsClient.GetPaymentByBookingIdAsync(intent.BookingId.Value, ct);
        if (payment != null && string.Equals(payment.Status, "Refunded", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Refund confirmed for booking #{BookingId}. Transitioning intent {IntentId} to Cancelled.",
                intent.BookingId.Value, intent.Id);
            intent.TransitionToCancelled();
        }
        else
        {
            // Permanecer en Cancelling hasta que Payments confirme el reembolso
            logger.LogInformation("Intent {IntentId} remains in Cancelling awaiting refund processing.", intent.Id);
            intent.NextRetryAtUtc = DateTime.UtcNow.AddSeconds(2);
            intent.Touch();
        }

        await db.SaveChangesAsync(ct);
    }

    internal static void ScheduleTransientRetry(PurchaseIntent intent)
    {
        intent.RetryCount++;
        var delaySeconds = intent.RetryCount switch
        {
            1 => 1,
            2 => 2,
            3 => 4,
            _ => 30 // Conciliación periódica cada 30 segundos
        };

        intent.NextRetryAtUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
        intent.Touch();
    }
}
