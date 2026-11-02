using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SmartStay.Payments.Infrastructure.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class PaymentsAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _roles;

    public PaymentsAuthorizeAttribute(params string[] roles)
    {
        _roles = roles;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var allowAnonymous = context.ActionDescriptor.EndpointMetadata
            .OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>()
            .Any();

        if (allowAnonymous) return;

        var user = context.HttpContext.Items["Actor"] as CurrentActor;
        if (user is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (_roles.Length > 0 && !_roles.Contains(user.Role, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
