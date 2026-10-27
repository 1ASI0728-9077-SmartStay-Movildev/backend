using Microsoft.AspNetCore.Authorization;

namespace SmartStay.Accommodations.Infrastructure.Security;

public class AccommodationsAuthenticationMiddleware(RequestDelegate next, ILogger<AccommodationsAuthenticationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IIdentityVerificationClient identityClient)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        var validation = await identityClient.ValidateTokenAsync(token);

        if (!validation.IsValid)
        {
            logger.LogWarning("Token validation rejected by IAM: {Reason}", validation.Reason);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync($"Unauthorized: {validation.Reason ?? "Invalid token"}");
            return;
        }

        var actor = new CurrentActor(
            validation.UserId ?? 0,
            validation.Username ?? string.Empty,
            validation.Role ?? string.Empty,
            validation.HotelId,
            validation.ChainId
        );

        context.Items["CurrentActor"] = actor;
        await next(context);
    }
}
