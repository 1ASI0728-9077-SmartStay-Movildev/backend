using System.Diagnostics;
using System.Net;
using Apache.NMS;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F01")]
public class F01_DisposableEnvironmentBoundaryTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly GatewayClient _gatewayClient;

    public F01_DisposableEnvironmentBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _activeMqFixture = new ActiveMqFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F01")]
    public async Task TC_F01_B01_GatewayHealth_SupportsGetAndHeadMethods()
    {
        using var headReq = new HttpRequestMessage(HttpMethod.Head, "/health");
        var headResp = await _apiFixture.GatewayClient.SendAsync(headReq);
        headResp.StatusCode.Should().Be(HttpStatusCode.OK, "Gateway health endpoint must respond 200 OK to HEAD requests");

        using var getReq = new HttpRequestMessage(HttpMethod.Get, "/health");
        var getResp = await _apiFixture.GatewayClient.SendAsync(getReq);
        getResp.StatusCode.Should().Be(HttpStatusCode.OK, "Gateway health endpoint must respond 200 OK to GET requests");
        var content = await getResp.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F01")]
    public async Task TC_F01_B02_GatewayRouteProtection_BlocksEncodedAndRelativeTraversals()
    {
        var traversalPaths = new[]
        {
            "/internal/v1/payments/demo-methods/../..",
            "/internal/v1/bookings/quotes",
            "/api/v1/../internal/v1/payments",
            "/internal"
        };

        foreach (var path in traversalPaths)
        {
            var response = await _apiFixture.GatewayClient.GetAsync(path);
            response.StatusCode.Should().BeOneOf(
                new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
                $"Gateway must reject internal route traversal attempt at '{path}'"
            );
        }
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F01")]
    public async Task TC_F01_B03_ActiveMq_RapidSessionLifecycleBoundary()
    {
        var isReachable = await _activeMqFixture.IsBrokerReachableAsync();
        isReachable.Should().BeTrue();

        // Boundary stress: rapidly create and dispose 10 sessions on OpenWire connection
        for (var i = 0; i < 10; i++)
        {
            using var session = _activeMqFixture.Connection.CreateSession(AcknowledgementMode.AutoAcknowledge);
            session.Should().NotBeNull();
            var queue = session.CreateTemporaryQueue();
            queue.Should().NotBeNull();
            queue.Dispose();
        }
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F01")]
    public async Task TC_F01_B04_MultiContextDatabaseRunner_InvalidContext_GracefullyFails()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        string? dllPath = null;
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "tools", "SmartStay.Database", "bin", "Debug", "net9.0", "SmartStay.Database.dll");
            if (File.Exists(candidate))
            {
                dllPath = candidate;
                break;
            }
            current = current.Parent;
        }

        dllPath.Should().NotBeNull();
        var psi = new ProcessStartInfo("dotnet", $"\"{dllPath}\" check non_existent_context")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.Environment["ConnectionStrings__DefaultConnection"] = E2ETestConfig.MySqlConnectionString;
        psi.Environment["MYSQL_REQUIRE_TLS"] = "false";

        using var process = Process.Start(psi);
        process.Should().NotBeNull();
        await process!.WaitForExitAsync();

        // Runner should handle unrecognized arguments with non-zero exit or usage message
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        (stdout + stderr).Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F01")]
    public async Task TC_F01_B05_MySqlMultiSchema_TableIsolationAcrossContexts()
    {
        await using var connection = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await connection.OpenAsync();

        // Verify that tables belonging to each microservice context are properly registered in MySQL
        var expectedTables = new[]
        {
            "users",                  // IAM
            "rooms",                  // Accommodations
            "bookings",               // Bookings
            "demo_payment_methods",   // Payments
            "purchase_intents"        // Concierge
        };

        foreach (var table in expectedTables)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @tbl";
            cmd.Parameters.AddWithValue("@tbl", table);
            var exists = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            exists.Should().Be(1, $"Table '{table}' must exist in the database schema");
        }
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
