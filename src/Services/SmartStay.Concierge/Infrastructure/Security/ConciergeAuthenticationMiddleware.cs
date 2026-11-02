namespace SmartStay.Concierge.Infrastructure.Security;

public class ConciergeAuthenticationMiddleware(RequestDelegate next, ILogger<ConciergeAuthenticationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IIdentityVerificationClient identityClient)
    {
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            var identity = await identityClient.ValidateAsync(token);

            if (identity is null)
            {
                logger.LogError("IAM validation service is unreachable or returned error.");
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { message = "Identity verification service temporarily unavailable." });
                return;
            }

            if (!identity.IsValid)
            {
                logger.LogWarning("Token verification rejected: {Reason}", identity.Reason ?? "Invalid or revoked token.");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { message = identity.Reason ?? "Invalid or revoked token." });
                return;
            }

            var actor = new CurrentActor(
                UserId: identity.UserId ?? 0,
                Username: identity.Username ?? string.Empty,
                Role: identity.Role ?? string.Empty,
                HotelId: identity.HotelId,
                ChainId: identity.ChainId
            );

            context.Items["CurrentActor"] = actor;
        }

        await next(context);
    }
}
