namespace SmartStay.Payments.Infrastructure.Security;

public class PaymentsAuthenticationMiddleware(RequestDelegate next, ILogger<PaymentsAuthenticationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IIdentityVerificationClient identityClient)
    {
        var token = context.Request.Headers.Authorization.FirstOrDefault()?.Split(" ").Last();

        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var validation = await identityClient.ValidateTokenAsync(token);
                if (validation is not null && validation.IsValid && validation.UserId.HasValue)
                {
                    context.Items["Actor"] = new CurrentActor(
                        validation.UserId.Value,
                        validation.Username ?? string.Empty,
                        validation.Role ?? string.Empty,
                        validation.TokenVersion
                    );
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while verifying authorization token with IAM service.");
            }
        }

        await next(context);
    }
}
