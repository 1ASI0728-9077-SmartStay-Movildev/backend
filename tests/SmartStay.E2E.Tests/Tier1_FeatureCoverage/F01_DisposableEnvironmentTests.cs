using System.Diagnostics;
using System.Net;
using Apache.NMS;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F01")]
public class F01_DisposableEnvironmentTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly GatewayClient _gatewayClient;

    public F01_DisposableEnvironmentTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _activeMqFixture = new ActiveMqFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F01")]
    public async Task TC_F01_01_GatewayHealthcheck_ReturnsHealthy()
    {
        var response = await _gatewayClient.GetHealthAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "Gateway healthcheck endpoint /health must return 200 OK");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrWhiteSpace();
        content.ToLowerInvariant().Should().Contain("healthy");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F01")]
    public async Task TC_F01_02_ActiveMqOpenWire_SocketAndConnectionReachability()
    {
        var isReachable = await _activeMqFixture.IsBrokerReachableAsync();
        isReachable.Should().BeTrue("ActiveMQ OpenWire broker must be reachable at " + E2ETestConfig.ActiveMqBrokerUri);

        using var session = _activeMqFixture.Connection.CreateSession(AcknowledgementMode.AutoAcknowledge);
        session.Should().NotBeNull("ActiveMQ connection must produce a valid OpenWire session");

        var tempQueue = session.CreateTemporaryQueue();
        tempQueue.Should().NotBeNull("ActiveMQ broker must support temporary queue creation over OpenWire session");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F01")]
    public async Task TC_F01_03_MultiContextDatabaseRunner_CheckModeExecutesCleanly()
    {
        // 1. Verify all migration history tables exist in the live MySQL instance
        await using var connection = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await connection.OpenAsync();

        var expectedContexts = new[] { "iam", "accommodations", "bookings", "payments", "profiles", "analytics", "concierge" };
        foreach (var ctx in expectedContexts)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM `__EFMigrationsHistory_{ctx}`";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            count.Should().BeGreaterThan(0, $"Migration history table for context '{ctx}' must have applied migrations in live MySQL");
        }

        // 2. Execute database runner in check mode against live MySQL
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

        dllPath.Should().NotBeNull("SmartStay.Database.dll must exist in workspace build output");
        var psi = new ProcessStartInfo("dotnet", $"\"{dllPath}\" check")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.Environment["ConnectionStrings__DefaultConnection"] = E2ETestConfig.MySqlConnectionString;
        psi.Environment["MYSQL_REQUIRE_TLS"] = "false";

        using var process = Process.Start(psi);
        process.Should().NotBeNull();
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        process.ExitCode.Should().Be(0, $"Database check mode must exit cleanly against live MySQL. Stderr: {stderr}");
        stdout.Should().Contain("Connected to MySQL", "Runner must connect to live MySQL instance");
        stdout.Should().Contain("Existing tables:", "Runner must inspect existing tables in live MySQL");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F01")]
    public async Task TC_F01_04_LocalDevelopmentTlsBypass_ConfiguredCorrectly()
    {
        var response = await _gatewayClient.SearchRoomsAsync(district: "Lima", guests: 2);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "Local gateway must serve plain HTTP traffic without TLS enforcement");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F01")]
    public async Task TC_F01_05_GatewayRouteProtection_BlocksInternalEndpointsWith403()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/v1/payments/booking/1");
        var response = await _apiFixture.GatewayClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Gateway Nginx configuration blocks ^~ /internal/ routes with 403 Forbidden");
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
    }
}
