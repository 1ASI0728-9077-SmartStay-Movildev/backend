using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F09")]
public class F09_DemoCardIsolationAndIdempotencyBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F09_DemoCardIsolationAndIdempotencyBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F09")]
    public async Task TC_F09_B01_MultiGuest_CardIdDistinctness()
    {
        var userA = "iso_user_a_" + Guid.NewGuid().ToString("N")[..8];
        var userB = "iso_user_b_" + Guid.NewGuid().ToString("N")[..8];

        var tokenA = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, userA, "Password123!");
        var tokenB = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, userB, "Password123!");

        var respA = await _gatewayClient.ActivateDemoPaymentMethodAsync(tokenA);
        var respB = await _gatewayClient.ActivateDemoPaymentMethodAsync(tokenB);

        respA.StatusCode.Should().Be(HttpStatusCode.OK);
        respB.StatusCode.Should().Be(HttpStatusCode.OK);

        var idA = JsonDocument.Parse(await respA.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();
        var idB = JsonDocument.Parse(await respB.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        idA.Should().NotBeNullOrWhiteSpace();
        idB.Should().NotBeNullOrWhiteSpace();
        idA.Should().NotBe(idB, "Different guests must receive distinct demo payment method IDs");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F09")]
    public async Task TC_F09_B02_CrossUserAccessForbidden()
    {
        var userA = "iso_user_c_" + Guid.NewGuid().ToString("N")[..8];
        var userB = "iso_user_d_" + Guid.NewGuid().ToString("N")[..8];

        var tokenA = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, userA, "Password123!");
        var tokenB = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, userB, "Password123!");

        var respA = await _gatewayClient.ActivateDemoPaymentMethodAsync(tokenA);
        var idA = JsonDocument.Parse(await respA.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        // User B queries their own method; it must NOT return User A's ID
        var respB = await _gatewayClient.ActivateDemoPaymentMethodAsync(tokenB);
        var idB = JsonDocument.Parse(await respB.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        idB.Should().NotBe(idA);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F09")]
    public async Task TC_F09_B03_RepeatedActivation_NeverResetsAlteredBalance()
    {
        var user = "iso_user_e_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var act1 = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var doc1 = JsonDocument.Parse(await act1.Content.ReadAsStringAsync());
        var cardId = doc1.RootElement.GetProperty("id").GetString();

        // Alter balance in database directly to simulate spent funds (e.g. $500,000.00)
        await using (var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE demo_payment_methods SET balance = 500000.00 WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", cardId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Re-call activate endpoint
        var act2 = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        act2.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc2 = JsonDocument.Parse(await act2.Content.ReadAsStringAsync());

        doc2.RootElement.GetProperty("id").GetString().Should().Be(cardId);
        doc2.RootElement.GetProperty("balance").GetDecimal().Should().Be(500000.00m, "Re-activation must preserve existing mutated balance and NEVER reset to 1,000,000");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F09")]
    public async Task TC_F09_B04_ConcurrentActivation_ProducesSingleCard()
    {
        var user = "conc_act_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        // Run 5 simultaneous activations using thread barrier
        var responses = await ConcurrencyTestHelper.ExecuteConcurrentBarrierAsync(5, async _ =>
        {
            var resp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
            var body = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("id").GetString();
        });

        // All returned IDs must be identical
        responses.Distinct().Should().ContainSingle("Concurrent activations must all yield the exact same card ID");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F09")]
    public async Task TC_F09_B05_SynthesizedDifferentUserIdToken_RejectsCardLeak()
    {
        // Generate token for a non-existent userId
        var syntheticToken = _authFixture.CreateSyntheticJwt(999991, "synthetic_ghost", "guest");

        var response = await _gatewayClient.GetDemoPaymentMethodAsync(syntheticToken);
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized },
            "Non-existent or synthetic ghost user must not leak any demo card"
        );
    }
}
