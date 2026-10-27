using Humanizer;

namespace SmartStay.Bookings.Infrastructure.Persistence.Extensions;

public static class StringExtensions
{
    public static string ToSnakeCase(this string text)
    {
        return text.Underscore();
    }

    public static string ToPlural(this string text)
    {
        return text.Pluralize();
    }
}
