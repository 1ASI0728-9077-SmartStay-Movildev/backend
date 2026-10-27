using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Concierge.Application;
using SmartStay.Concierge.Infrastructure.Security;
using SmartStay.Contracts.Constants;
using Swashbuckle.AspNetCore.Annotations;

namespace SmartStay.Concierge.Interfaces.REST;

/// <summary>
/// Endpoint del asistente virtual Concierge impulsado por IA y validado con el catálogo de SmartStay.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("SmartStay Intelligent Concierge Endpoints")]
public class ConciergeController(RecommendRooms recommendRooms) : ControllerBase
{
    [HttpPost("messages")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Send message to Concierge for assisted room discovery and recommendations", OperationId = "SendMessageToConcierge")]
    [SwaggerResponse(StatusCodes.Status200OK, "Assisted recommendation response with verified offers.", typeof(ConciergeRecommendationResponse))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid request parameters.")]
    public async Task<IActionResult> SendMessage([FromBody] ConciergeRecommendationRequest request, CancellationToken ct)
    {
        if (request.CheckIn >= request.CheckOut)
        {
            return BadRequest(new { message = "CheckIn date must be before CheckOut date." });
        }

        var nights = request.CheckOut.DayNumber - request.CheckIn.DayNumber;
        if (nights is < 1 or > 30)
        {
            return BadRequest(new { message = "Stay duration must be between 1 and 30 nights for MVP." });
        }

        var response = await recommendRooms.ExecuteAsync(request, ct);
        return Ok(response);
    }
}
