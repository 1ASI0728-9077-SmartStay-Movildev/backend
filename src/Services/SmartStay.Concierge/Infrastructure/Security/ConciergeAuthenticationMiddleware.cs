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

            if (identity != null && identity.IsValid)
            {
                var actor = new CurrentActor(
                    UserId: identity.UserId ?? 0,
                    Username: identity.Username ?? string.Empty,
                    Role: identity.Role ?? string.Empty,
                    HotelId: identity.HotelId,
                    ChainId: identity.ChainId
                );

                context.Items["CurrentActor"] = actor;
            }
            else
            {
                logger.LogWarning("Token verification failed or session was revoked: {Reason}", identity?.Reason ?? "Service unavailable");
            }
        }

        await next(context);
    }
}
