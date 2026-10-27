using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SmartStay.Bookings.Infrastructure.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class BookingsAuthorizeAttribute(params string[] roles) : Attribute, IAuthorizationFilter
{
    private readonly string[] _roles = roles;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var allowAnonymous = context.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any();
        if (allowAnonymous) return;

        var actor = context.HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (_roles.Length == 0) return;

        var hasRole = _roles.Any(r => string.Equals(r, actor.Role, StringComparison.OrdinalIgnoreCase));
        if (!hasRole)
        {
            context.Result = new ForbidResult();
        }
    }
}
