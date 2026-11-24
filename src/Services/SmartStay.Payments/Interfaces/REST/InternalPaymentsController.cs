using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Contracts.Http;
using SmartStay.Payments.Domain.Model.Queries;
using SmartStay.Payments.Domain.Services;
using SmartStay.Payments.Interfaces.REST.Transform;

namespace SmartStay.Payments.Interfaces.REST;

[ApiController]
[Route("internal/v1/payments")]
[Produces(MediaTypeNames.Application.Json)]
public class InternalPaymentsController(IPaymentQueryService paymentQueryService) : ControllerBase
{
    [HttpGet("booking/{bookingId:int}")]
    public async Task<IActionResult> GetByBookingId(int bookingId)
    {
        var payment = await paymentQueryService.Handle(new GetPaymentByBookingIdQuery(bookingId));
        if (payment is null) return NotFound();
        return Ok(PaymentAssemblers.ToResourceFromEntity(payment));
    }

    [HttpGet("demo-methods/{id}/validate")]
    public async Task<IActionResult> ValidateDemoPaymentMethod(
        [FromRoute] string id,
        [FromQuery] int guestUserId)
    {
        var (isValid, reason, method) = await paymentQueryService.Handle(new ValidateDemoPaymentMethodQuery(id, guestUserId));

        if (!isValid)
        {
            if (method is null)
            {
                return NotFound(new ValidateDemoPaymentMethodResponse(
                    IsValid: false,
                    Reason: reason
                ));
            }

            return UnprocessableEntity(new ValidateDemoPaymentMethodResponse(
                IsValid: false,
                Status: method.Status,
                Balance: method.Balance,
                Currency: method.Currency,
                Reason: reason
            ));
        }

        return Ok(new ValidateDemoPaymentMethodResponse(
            IsValid: true,
            Status: method!.Status,
            Balance: method.Balance,
            Currency: method.Currency
        ));
    }
}
