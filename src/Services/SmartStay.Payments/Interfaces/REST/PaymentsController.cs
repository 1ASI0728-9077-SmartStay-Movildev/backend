using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Contracts.Constants;
using SmartStay.Payments.Domain.Model.Commands;
using SmartStay.Payments.Domain.Model.Queries;
using SmartStay.Payments.Domain.Services;
using SmartStay.Payments.Infrastructure.Security;
using SmartStay.Payments.Interfaces.REST.Resources;
using SmartStay.Payments.Interfaces.REST.Transform;
using Swashbuckle.AspNetCore.Annotations;

namespace SmartStay.Payments.Interfaces.REST;

[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Available Payment Endpoints")]
public class PaymentsController(
    IPaymentCommandService paymentCommandService,
    IPaymentQueryService paymentQueryService) : ControllerBase
{
    [HttpPost]
    [PaymentsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(
        Summary = "Process a new payment transaction with idempotency and outbox publishing",
        Description = "Simulates and records a credit card or demo card payment transaction for an active booking obligation.",
        OperationId = "ProcessPayment")]
    [SwaggerResponse(StatusCodes.Status201Created, "Payment was successfully processed and queued for asynchronous confirmation.", typeof(PaymentResource))]
    [SwaggerResponse(StatusCodes.Status200OK, "Idempotent replay: previous payment transaction returned.", typeof(PaymentResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The provided processing resource layout is invalid.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "Idempotency conflict or payment obligation already satisfied.")]
    public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentResource resource)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault() ?? Guid.NewGuid().ToString();
        var actor = HttpContext.Items["Actor"] as CurrentActor;
        var actorId = actor?.Id ?? 0;

        var command = new ProcessPaymentCommand(
            resource.BookingId,
            resource.Amount,
            resource.PaymentMethod,
            resource.CardNumber ?? string.Empty,
            resource.CardHolderName ?? string.Empty,
            resource.ExpirationDate ?? string.Empty,
            resource.Cvv ?? string.Empty,
            idempotencyKey,
            actorId,
            resource.DemoPaymentMethodId
        );

        var (payment, isDuplicate, conflictReason) = await paymentCommandService.Handle(command);

        if (conflictReason is not null)
        {
            return Conflict(new { message = conflictReason });
        }

        if (payment is null)
        {
            return BadRequest(new { message = "Could not process payment transaction aggregate." });
        }

        var responseResource = PaymentAssemblers.ToResourceFromEntity(payment);

        if (isDuplicate)
        {
            return Ok(responseResource);
        }

        return CreatedAtAction(nameof(GetPaymentByBooking), new { bookingId = payment.BookingId }, responseResource);
    }

    [HttpGet("booking/{bookingId:int}")]
    [PaymentsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin, UserRoles.Guest)]
    [SwaggerOperation(
        Summary = "Get payment ledger properties by booking aggregate identifier",
        Description = "Retrieves structural transaction records and authorization metadata.",
        OperationId = "GetPaymentByBooking")]
    [SwaggerResponse(StatusCodes.Status200OK, "The payment aggregate associated with the booking context was located.", typeof(PaymentResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No payment transaction matched the supplied criteria.")]
    public async Task<IActionResult> GetPaymentByBooking(int bookingId)
    {
        var query = new GetPaymentByBookingIdQuery(bookingId);
        var payment = await paymentQueryService.Handle(query);

        if (payment is null) return NotFound();
        return Ok(PaymentAssemblers.ToResourceFromEntity(payment));
    }

    [HttpPost("demo-method")]
    [RequireDemoPayment]
    [PaymentsAuthorize(UserRoles.Guest)]
    [SwaggerOperation(
        Summary = "Activate demo payment method for authenticated guest",
        Description = "Creates or returns the active demo payment method for the authenticated guest.",
        OperationId = "ActivateDemoPaymentMethod")]
    [SwaggerResponse(StatusCodes.Status200OK, "Demo payment method activated or already existing.", typeof(DemoPaymentMethodResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Unauthenticated caller.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Forbidden: demo payment feature disabled or caller not a guest.")]
    public async Task<IActionResult> ActivateDemoPaymentMethod()
    {
        var actor = HttpContext.Items["Actor"] as CurrentActor;
        if (actor is null) return Unauthorized();

        var command = new ActivateDemoPaymentMethodCommand(actor.Id);
        var method = await paymentCommandService.Handle(command);
        return Ok(PaymentAssemblers.ToResourceFromEntity(method));
    }

    [HttpGet("demo-method")]
    [RequireDemoPayment]
    [PaymentsAuthorize(UserRoles.Guest)]
    [SwaggerOperation(
        Summary = "Get demo payment method for authenticated guest",
        Description = "Retrieves the active demo payment method belonging to the authenticated guest.",
        OperationId = "GetDemoPaymentMethod")]
    [SwaggerResponse(StatusCodes.Status200OK, "Demo payment method found.", typeof(DemoPaymentMethodResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Unauthenticated caller.")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Forbidden: demo payment feature disabled or caller not a guest.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No active demo payment method found for guest.")]
    public async Task<IActionResult> GetDemoPaymentMethod()
    {
        var actor = HttpContext.Items["Actor"] as CurrentActor;
        if (actor is null) return Unauthorized();

        var query = new GetDemoPaymentMethodByGuestUserIdQuery(actor.Id);
        var method = await paymentQueryService.Handle(query);
        if (method is null)
            return NotFound(new { message = "No active demo payment method found for guest." });

        return Ok(PaymentAssemblers.ToResourceFromEntity(method));
    }
}
