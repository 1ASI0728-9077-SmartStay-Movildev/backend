using Microsoft.AspNetCore.Mvc.ApplicationModels;
using SmartStay.Analytics.Infrastructure.Web.Extensions;

namespace SmartStay.Analytics.Infrastructure.Web;

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
