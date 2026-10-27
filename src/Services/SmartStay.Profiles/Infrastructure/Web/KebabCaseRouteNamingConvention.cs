using Microsoft.AspNetCore.Mvc.ApplicationModels;
using SmartStay.Profiles.Infrastructure.Web.Extensions;

namespace SmartStay.Profiles.Infrastructure.Web;

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
