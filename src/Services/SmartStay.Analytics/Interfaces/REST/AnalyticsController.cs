using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Analytics.Domain.Model.Queries;
using SmartStay.Analytics.Domain.Services;
using SmartStay.Analytics.Infrastructure.Security;
using SmartStay.Analytics.Interfaces.REST.Resources;
using SmartStay.Analytics.Interfaces.REST.Transform;
using SmartStay.Contracts.Constants;
using Swashbuckle.AspNetCore.Annotations;

namespace SmartStay.Analytics.Interfaces.REST;

[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Available Analytics Endpoints")]
public class AnalyticsController(IAnalyticsQueryService analyticsQueryService) : ControllerBase
{
    [HttpGet("performance/monthly")]
    [AnalyticsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(
        Summary = "Get monthly performance metrics",
        Description = "Retrieves aggregated metrics like revenue and occupancy for the current month from local projection tables.",
        OperationId = "GetMonthlyPerformance")]
    [SwaggerResponse(StatusCodes.Status200OK, "Monthly metrics", typeof(PerformanceMetricsResource))]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Missing or invalid JWT Token")]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "User does not have required permissions (Admin/ChainAdmin)")]
    public async Task<IActionResult> GetMonthlyPerformance()
    {
        var metrics = await analyticsQueryService.Handle(new GetMonthlyPerformanceQuery());
        return Ok(PerformanceMetricsAssembler.ToResourceFromEntity(metrics));
    }
}
