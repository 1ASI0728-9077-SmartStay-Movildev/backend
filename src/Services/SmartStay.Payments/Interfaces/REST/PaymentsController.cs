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
        Description = "Simulates and records a credit card payment transaction for an active booking obligation.",
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
            resource.CardNumber,
            resource.CardHolderName,
            resource.ExpirationDate,
            resource.Cvv,
            idempotencyKey,
            actorId
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
}
