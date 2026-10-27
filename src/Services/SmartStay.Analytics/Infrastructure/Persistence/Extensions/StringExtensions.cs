using System.Text.RegularExpressions;

namespace SmartStay.Analytics.Infrastructure.Persistence.Extensions;

public static class StringExtensions
{
    public static string ToSnakeCase(this string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var startUnderscores = Regex.Match(text, @"^_+");
        return startUnderscores + Regex.Replace(text, @"([a-z0-9])([A-Z])", "$1_$2").ToLower();
    }
}
