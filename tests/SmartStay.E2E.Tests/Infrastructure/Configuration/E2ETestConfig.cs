namespace SmartStay.E2E.Tests.Infrastructure.Configuration;

/// <summary>
/// Centralized configuration provider for SmartStay E2E test suite.
/// Supports override via environment variables with defaults matching Docker Compose topology.
/// </summary>
public static class E2ETestConfig
{
    public static string GatewayUrl =>
        (Environment.GetEnvironmentVariable("SMARTSTAY_GATEWAY_URL") ?? "http://localhost:8080").TrimEnd('/');

    public static string DirectBookingsUrl =>
        (Environment.GetEnvironmentVariable("SMARTSTAY_BOOKINGS_URL") ?? "http://localhost:5004").TrimEnd('/');

    public static string DirectPaymentsUrl =>
        (Environment.GetEnvironmentVariable("SMARTSTAY_PAYMENTS_URL") ?? "http://localhost:5005").TrimEnd('/');

    public static string DirectConciergeUrl =>
        (Environment.GetEnvironmentVariable("SMARTSTAY_CONCIERGE_URL") ?? "http://localhost:5008").TrimEnd('/');

    public static string DirectIamUrl =>
        (Environment.GetEnvironmentVariable("SMARTSTAY_IAM_URL") ?? "http://localhost:5001").TrimEnd('/');

    public static string DirectAccommodationsUrl =>
        (Environment.GetEnvironmentVariable("SMARTSTAY_ACCOMMODATIONS_URL") ?? "http://localhost:5002").TrimEnd('/');

    public static string ActiveMqBrokerUri =>
        Environment.GetEnvironmentVariable("ACTIVEMQ_BROKER_URI") ?? "tcp://localhost:61616";

    public static string ActiveMqWebConsoleUrl =>
        Environment.GetEnvironmentVariable("ACTIVEMQ_WEB_CONSOLE_URL") ?? "http://localhost:8161";

    public static string ActiveMqUser =>
        Environment.GetEnvironmentVariable("ACTIVEMQ_USER") ?? "admin";

    public static string ActiveMqPassword =>
        Environment.GetEnvironmentVariable("ACTIVEMQ_PASSWORD") ?? "admin";

    public static string DefaultPassword =>
        Environment.GetEnvironmentVariable("SMARTSTAY_DEFAULT_PASSWORD") ?? "Password123!";

    public static string JwtSecret =>
        Environment.GetEnvironmentVariable("JWT_SECRET") ?? "EstaEsUnaClaveSecretaMuyLargaYSeguraParaSmartStay123!";

    public static string JwtIssuer =>
        Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "http://localhost:5192";

    public static string JwtAudience =>
        Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "http://localhost:5173";

    public static string MySqlConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
        ?? Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING")
        ?? "Server=127.0.0.1;Port=3308;Database=smartstay_legacy;Uid=root;Pwd=rootpassword;SslMode=None;";

    public static int HttpTimeoutSeconds =>
        int.TryParse(Environment.GetEnvironmentVariable("SMARTSTAY_HTTP_TIMEOUT_SECONDS"), out var sec) ? sec : 30;
}
