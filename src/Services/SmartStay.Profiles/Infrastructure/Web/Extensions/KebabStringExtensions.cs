using System.Text.RegularExpressions;

namespace SmartStay.Profiles.Infrastructure.Web.Extensions;

public static class KebabStringExtensions
{
    public static string ToKebabCase(this string text)
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
