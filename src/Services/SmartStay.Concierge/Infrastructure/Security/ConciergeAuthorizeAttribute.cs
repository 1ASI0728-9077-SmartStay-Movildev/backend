using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SmartStay.Concierge.Infrastructure.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ConciergeAuthorizeAttribute(params string[] roles) : Attribute, IAuthorizationFilter
{
    private readonly string[] _allowedRoles = roles;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var allowAnonymous = context.ActionDescriptor.EndpointMetadata
            .OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>()
            .Any();

        if (allowAnonymous) return;

        var actor = context.HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor == null)
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Authentication token missing, invalid or revoked." });
            return;
        }

        if (_allowedRoles.Length > 0 && !_allowedRoles.Contains(actor.Role, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
