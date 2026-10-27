using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
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
}
