using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace SmartStay.IAM.Infrastructure.Web;

public class KebabCaseRouteNamingConvention : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        foreach (var selector in controller.Selectors)
        {
            if (selector.AttributeRouteModel != null)
            {
                selector.AttributeRouteModel.Template = selector.AttributeRouteModel.Template?
                    .Replace("[controller]", ToKebabCase(controller.ControllerName));
            }
        }
    }

    private static string ToKebabCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return Regex.Replace(
            text,
            "(?<!^)([A-Z][a-z]|(?<=[a-z])[A-Z0-9])",
            "-$1",
            RegexOptions.Compiled).Trim().ToLower();
    }
}
