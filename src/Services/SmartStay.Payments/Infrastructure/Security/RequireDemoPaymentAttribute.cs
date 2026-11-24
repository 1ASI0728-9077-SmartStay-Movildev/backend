using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartStay.Payments.Infrastructure.Configuration;

namespace SmartStay.Payments.Infrastructure.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireDemoPaymentAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices.GetService<IOptions<PaymentFeaturesOptions>>();
        if (options == null || !options.Value.EnableDemoPayment)
        {
            context.Result = new ObjectResult(new { message = "Demo payment feature is disabled." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
