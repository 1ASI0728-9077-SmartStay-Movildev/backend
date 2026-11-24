using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F09")]
public class F09_DemoCardIsolationAndIdempotencyTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F09_DemoCardIsolationAndIdempotencyTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F09")]
    public async Task TC_F09_01_MultiGuest_CardIsolation()
    {
        var guest1Token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var guest2Token = await _authFixture.GetGuest2TokenAsync(_apiFixture.GatewayClient);

        var resp1 = await _gatewayClient.ActivateDemoPaymentMethodAsync(guest1Token);
        var resp2 = await _gatewayClient.ActivateDemoPaymentMethodAsync(guest2Token);

        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync());
        var doc2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());

        var card1Id = doc1.RootElement.GetProperty("id").GetString();
        var card2Id = doc2.RootElement.GetProperty("id").GetString();

        card1Id.Should().NotBe(card2Id, "Guest 1 and Guest 2 must have distinct, isolated demo payment method identifiers");
        doc1.RootElement.GetProperty("guestUserId").GetInt32().Should().Be(2);
        doc2.RootElement.GetProperty("guestUserId").GetInt32().Should().Be(3);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F09")]
    public async Task TC_F09_02_IdempotentActivation_ReturnsSameCardId()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var resp1 = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var resp2 = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);

        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync());
        var doc2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());

        var id1 = doc1.RootElement.GetProperty("id").GetString();
        var id2 = doc2.RootElement.GetProperty("id").GetString();

        id1.Should().Be(id2, "Repeated activations for the same guest must return the identical card ID");
        doc1.RootElement.GetProperty("balance").GetDecimal().Should().Be(doc2.RootElement.GetProperty("balance").GetDecimal());
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F09")]
    public async Task TC_F09_03_RepeatedActivation_PreservesMutatedBalanceContract()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var initialResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        initialResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var initialDoc = JsonDocument.Parse(await initialResp.Content.ReadAsStringAsync());
        var cardId = initialDoc.RootElement.GetProperty("id").GetString();

        // 1. Create a booking to attach payment debit
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(60);
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "F09 Mutated Balance Test",
            guestEmail = "f09-balance@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookJson = await bookResp.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = bookJson.GetProperty("id").GetInt32();
        var totalAmount = bookJson.GetProperty("totalAmount").GetDecimal();

        // 2. Process debit of exact totalAmount
        var debitPayload = new
        {
            bookingId,
            amount = totalAmount,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        };
        var debitResp = await _gatewayClient.ProcessPaymentAsync(debitPayload, token, "pay-mutated-" + Guid.NewGuid());
        debitResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // 3. Re-activate demo method: must preserve existing balance and NOT reset to 1M
        var reactivateResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        reactivateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var reactivateDoc = JsonDocument.Parse(await reactivateResp.Content.ReadAsStringAsync());
        reactivateDoc.RootElement.GetProperty("id").GetString().Should().Be(cardId);
        reactivateDoc.RootElement.GetProperty("balance").GetDecimal().Should().BeLessThan(1000000.00m, "Re-activation must never reset a mutated balance back to $1,000,000.00");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F09")]
    public async Task TC_F09_04_ConcurrentActivation_BarrierSafety()
    {
        const int concurrentWorkers = 5;
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);

        var responses = await ConcurrencyTestHelper.ExecuteConcurrentBarrierAsync(
            concurrentWorkers,
            async _ => await _gatewayClient.ActivateDemoPaymentMethodAsync(token),
            TimeSpan.FromSeconds(10)
        );

        responses.Should().HaveCount(concurrentWorkers);
        responses.All(r => r.StatusCode == HttpStatusCode.OK).Should().BeTrue("All concurrent activation requests must return 200 OK");

        var ids = new List<string>();
        foreach (var resp in responses)
        {
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            ids.Add(doc.RootElement.GetProperty("id").GetString()!);
        }

        ids.Distinct().Should().ContainSingle("All concurrent activations must return the exact same payment method aggregate");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F09")]
    public async Task TC_F09_05_QueryParameterSpoofing_Resistance()
    {
        // Authenticate as Guest 2, attempt to query ?guestUserId=2 (Guest 1)
        var guest2Token = await _authFixture.GetGuest2TokenAsync(_apiFixture.GatewayClient);
        // Ensure Guest 2 has activated a demo method
        await _gatewayClient.ActivateDemoPaymentMethodAsync(guest2Token);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/payments/demo-method?guestUserId=2");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", guest2Token);

        var resp = await _apiFixture.GatewayClient.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("guestUserId").GetInt32().Should().Be(3, "Identity must be derived exclusively from JWT claims (UserId=3 for Guest2), ignoring query parameter spoofing");
    }
}
