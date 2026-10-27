using Microsoft.AspNetCore.Mvc.ApplicationModels;
using SmartStay.Payments.Infrastructure.Web.Extensions;

namespace SmartStay.Payments.Infrastructure.Web;

public class KebabCaseRouteNamingConvention : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        foreach (var selector in controller.Selectors)
        {
            if (selector.AttributeRouteModel != null)
            {
                selector.AttributeRouteModel.Template = selector.AttributeRouteModel.Template?
                    .Replace("[controller]", controller.ControllerName.ToKebabCase());
            }
        }
    }
}
