using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartStay.Concierge.Domain;
using SmartStay.Concierge.Infrastructure.Clients;
using SmartStay.Concierge.Infrastructure.Persistence;
using SmartStay.Concierge.Infrastructure.Security;
using SmartStay.Concierge.Interfaces.REST.Resources;
using SmartStay.Contracts.Constants;
using Swashbuckle.AspNetCore.Annotations;

namespace SmartStay.Concierge.Interfaces.REST;

[ApiController]
[Route("api/v1/concierge/purchase-intents")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Concierge Autonomous Purchase Intent Orchestration")]
[ConciergeAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
public class PurchaseIntentsController(
    ConciergeDbContext dbContext,
    IPaymentsValidationClient paymentsValidationClient,
    IBookingsClient bookingsClient,
    ILogger<PurchaseIntentsController> logger) : ControllerBase
{
    private static PurchaseIntentTrackingResource ToTrackingResource(PurchaseIntent intent)
    {
        return new PurchaseIntentTrackingResource(
            Id: intent.Id,
            GuestUserId: intent.GuestUserId,
            Status: intent.Status.ToString(),
            CheckIn: intent.CheckIn.ToString("yyyy-MM-dd"),
            CheckOut: intent.CheckOut.ToString("yyyy-MM-dd"),
            District: intent.District,
            Guests: intent.Guests,
            BudgetTotal: intent.BudgetTotal,
            Currency: intent.Currency,
            DemoPaymentMethodId: intent.DemoPaymentMethodId,
            CancellationPolicyAccepted: intent.CancellationPolicyAccepted,
            RoomId: intent.SelectedRoomId,
            RoomTypeName: intent.SelectedRoomTypeName,
            HotelName: intent.SelectedHotelName,
            StayTotal: intent.StayTotal,
            QuoteId: intent.QuoteId,
            BookingId: intent.BookingId,
            PaymentId: intent.PaymentId,
            ReceiptVoucher: intent.ReceiptVoucher,
            FailureReason: intent.FailureReason,
            TotalAmountPaid: intent.StayTotal,
            UpdatedCardBalance: null,
            ExpiresAtUtc: intent.ExpiresAtUtc,
            CreatedAtUtc: intent.CreatedAtUtc
        );
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Create autonomous purchase intent mandate", OperationId = "CreatePurchaseIntent")]
    [SwaggerResponse(StatusCodes.Status202Accepted, "Mandate accepted for autonomous orchestration", typeof(PurchaseIntentTrackingResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid dates, terms, or budget parameters")]
    [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, "Demo card validation failure or unowned card")]
    public async Task<IActionResult> CreatePurchaseIntent([FromBody] CreatePurchaseIntentResource resource)
    {
        var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
        var userId = actor?.UserId ?? 0;
        if (userId <= 0) return Unauthorized();

        // 1. Verificar política de cancelación obligatoria
        if (!resource.CancellationPolicyAccepted)
        {
            return BadRequest(new { message = "Cancellation policy must be accepted to authorize autonomous purchase mandate." });
        }

        // 2. Validar rango de fechas y parámetros
        if (!DateOnly.TryParse(resource.CheckIn, out var checkIn) || !DateOnly.TryParse(resource.CheckOut, out var checkOut))
        {
            return BadRequest(new { message = "CheckIn and CheckOut must be valid dates in yyyy-MM-dd format." });
        }

        if (checkIn >= checkOut)
        {
            return BadRequest(new { message = "CheckIn date must be strictly before CheckOut date." });
        }

        var stayDuration = checkOut.DayNumber - checkIn.DayNumber;
        if (stayDuration is < 1 or > 30)
        {
            return BadRequest(new { message = "Stay duration must be between 1 and 30 nights for MVP." });
        }

        if (resource.Guests < 1)
        {
            return BadRequest(new { message = "Guests count must be at least 1." });
        }

        // 3. Validar presencia de demoPaymentMethodId
        if (string.IsNullOrWhiteSpace(resource.DemoPaymentMethodId))
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new
            {
                message = "Demo payment method ID is required.",
                reason = "MissingDemoPaymentMethodId"
            });
        }

        // 4. Guardia estricta: Verificar titularidad, vigencia y estado con Payments antes de abrir mandato
        var validation = await paymentsValidationClient.ValidateDemoPaymentMethodAsync(
            resource.DemoPaymentMethodId,
            userId,
            HttpContext.RequestAborted);

        if (!validation.IsValid)
        {
            logger.LogWarning("Demo card {CardId} validation failed for user {UserId}: {Reason}",
                resource.DemoPaymentMethodId, userId, validation.Reason);

            // DETENER FLUJO: 0 registros, 0 cotizaciones, 0 cerrojos
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new
            {
                message = validation.Reason ?? "Payment method not found, inactive, or not owned by guest.",
                reason = validation.Reason ?? "Payment method not found, inactive, or not owned by guest."
            });
        }

        // 5. Manejo de idempotencia: devolver existente si coincide clave
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await dbContext.PurchaseIntents
                .FirstOrDefaultAsync(p => p.GuestUserId == userId && p.IdempotencyKey == idempotencyKey, HttpContext.RequestAborted);

            if (existing is not null)
            {
                logger.LogInformation("Idempotent replay for user {UserId} with key '{Key}'. Returning intent {IntentId}.",
                    userId, idempotencyKey, existing.Id);
                return Accepted($"/api/v1/concierge/purchase-intents/{existing.Id}", ToTrackingResource(existing));
            }
        }

        // 6. Crear mandato duradero con TTL de 15 minutos
        var intentId = $"cpi_{Guid.NewGuid():N}";
        var intent = new PurchaseIntent(
            id: intentId,
            guestUserId: userId,
            idempotencyKey: string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey,
            demoPaymentMethodId: resource.DemoPaymentMethodId,
            checkIn: checkIn,
            checkOut: checkOut,
            guests: resource.Guests,
            district: resource.District,
            budgetTotal: resource.BudgetTotal,
            currency: string.IsNullOrWhiteSpace(resource.Currency) ? "USD" : resource.Currency,
            contactName: resource.ContactName ?? string.Empty,
            contactEmail: resource.ContactEmail,
            contactPhone: resource.ContactPhone,
            cancellationPolicyAccepted: resource.CancellationPolicyAccepted,
            mandateTtlMinutes: 15
        );

        await dbContext.PurchaseIntents.AddAsync(intent, HttpContext.RequestAborted);
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);

        logger.LogInformation("Autonomous purchase intent {IntentId} registered for user {UserId} (Mandate TTL: 15m).",
            intent.Id, userId);

        return Accepted($"/api/v1/concierge/purchase-intents/{intent.Id}", ToTrackingResource(intent));
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Get purchase intent tracking details", OperationId = "GetPurchaseIntentById")]
    [SwaggerResponse(StatusCodes.Status200OK, "Tracking details found", typeof(PurchaseIntentTrackingResource))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Forbidden to access another user's purchase intent")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Intent not found")]
    public async Task<IActionResult> GetPurchaseIntentById([FromRoute] string id)
    {
        var intent = await dbContext.PurchaseIntents.FirstOrDefaultAsync(p => p.Id == id, HttpContext.RequestAborted);
        if (intent is null)
        {
            return NotFound(new { message = $"Purchase intent '{id}' not found." });
        }

        var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor is null || (!actor.IsAdmin && !actor.IsChainAdmin && intent.GuestUserId != actor.UserId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden to access another user's purchase intent." });
        }

        return Ok(ToTrackingResource(intent));
    }

    [HttpPost("{id}/cancel")]
    [SwaggerOperation(Summary = "Cancel purchase intent mandate", OperationId = "CancelPurchaseIntent")]
    [SwaggerResponse(StatusCodes.Status200OK, "Purchase intent cancelled", typeof(PurchaseIntentTrackingResource))]
    [SwaggerResponse(StatusCodes.Status202Accepted, "Cancellation initiated; awaiting refund confirmation", typeof(PurchaseIntentTrackingResource))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Forbidden to cancel another user's purchase intent")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Intent not found")]
    public async Task<IActionResult> CancelPurchaseIntent([FromRoute] string id)
    {
        var intent = await dbContext.PurchaseIntents.FirstOrDefaultAsync(p => p.Id == id, HttpContext.RequestAborted);
        if (intent is null)
        {
            return NotFound(new { message = $"Purchase intent '{id}' not found." });
        }

        var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor is null || (!actor.IsAdmin && !actor.IsChainAdmin && intent.GuestUserId != actor.UserId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden to cancel another user's purchase intent." });
        }

        if (intent.Status == PurchaseIntentStatus.Cancelled)
        {
            return Ok(ToTrackingResource(intent));
        }

        // Si la reserva ya fue pagada (Confirmed o paymentId presente), pasar a Cancelling
        if (intent.Status == PurchaseIntentStatus.Confirmed || intent.PaymentId.HasValue)
        {
            if (intent.BookingId.HasValue)
            {
                await bookingsClient.CancelBookingAsync(intent.BookingId.Value, "Cancelled by user (Full refund demo policy)", intent.GuestUserId, HttpContext.RequestAborted);
            }

            intent.TransitionToCancelling();
            await dbContext.SaveChangesAsync(HttpContext.RequestAborted);

            logger.LogInformation("Intent {IntentId} transitioned to Cancelling. Awaiting refund confirmation.", intent.Id);
            return Accepted($"/api/v1/concierge/purchase-intents/{intent.Id}", ToTrackingResource(intent));
        }

        // Si aún no se pagó, cancelar reserva (liberar cerrojos de noches) y pasar directamente a Cancelled
        if (intent.BookingId.HasValue)
        {
            await bookingsClient.CancelBookingAsync(intent.BookingId.Value, "Cancelled by user before payment", intent.GuestUserId, HttpContext.RequestAborted);
        }

        intent.TransitionToCancelled();
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);

        logger.LogInformation("Intent {IntentId} cancelled successfully.", intent.Id);
        return Ok(ToTrackingResource(intent));
    }
}
