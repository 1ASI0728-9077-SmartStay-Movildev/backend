using System.Net;
using System.Text.Json;
using Apache.NMS;
using MySqlConnector;
using SmartStay.Contracts.Events.V1;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F12")]
public class F12_RefundBalanceRestorationBoundaryTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly GatewayClient _gatewayClient;

    public F12_RefundBalanceRestorationBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _activeMqFixture = new ActiveMqFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    private async Task<(int BookingId, int PaymentId, decimal TotalAmount, string DemoMethodId)> SetupPaidBookingAsync(string token)
    {
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync());
        var demoMethodId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "Refund Guest",
            guestEmail = "refund@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal();

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = demoMethodId
        }, token);
        payResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var payDoc = JsonDocument.Parse(await payResp.Content.ReadAsStringAsync());
        var paymentId = payDoc.RootElement.GetProperty("id").GetInt32();

        return (bookingId, paymentId, total, demoMethodId);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F12")]
    public async Task TC_F12_B01_FullRefund_ExactAlgebraicRestoration()
    {
        var user = "bnd_ref_1_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var (bookingId, paymentId, total, cardId) = await SetupPaidBookingAsync(token);

        // Get mid-balance after payment
        var cardResp1 = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var midBal = JsonDocument.Parse(await cardResp1.Content.ReadAsStringAsync()).RootElement.GetProperty("balance").GetDecimal();

        // Emit refund request event
        var eventId = Guid.NewGuid();
        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Bookings.V1",
            new PaymentRefundRequestedV1(
                PaymentId: paymentId,
                BookingId: bookingId,
                Reason: "Full refund test"
            ),
            "bookings.payment-refund-requested",
            eventId
        );

        // Poll for balance restoration
        var restored = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("balance").GetDecimal();
            },
            bal => bal == midBal + total,
            TimeSpan.FromSeconds(8),
            description: "Awaiting balance restoration"
        );

        restored.Should().Be(midBal + total, "Balance must be restored by exactly total booking amount");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F12")]
    public async Task TC_F12_B02_DuplicateRefundEvents_DoNotInflateBalance()
    {
        var user = "bnd_ref_2_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var (bookingId, paymentId, total, _) = await SetupPaidBookingAsync(token);

        var cardResp1 = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var midBal = JsonDocument.Parse(await cardResp1.Content.ReadAsStringAsync()).RootElement.GetProperty("balance").GetDecimal();

        var eventId = Guid.NewGuid();
        var refundEvt = new PaymentRefundRequestedV1(
            PaymentId: paymentId,
            BookingId: bookingId,
            Reason: "Duplicate test"
        );

        // First delivery
        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Bookings.V1",
            refundEvt,
            "bookings.payment-refund-requested",
            eventId
        );

        await Task.Delay(1500);

        // Second duplicate delivery
        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Bookings.V1",
            refundEvt,
            "bookings.payment-refund-requested",
            eventId
        );

        await Task.Delay(1500);

        var cardResp2 = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBal = JsonDocument.Parse(await cardResp2.Content.ReadAsStringAsync()).RootElement.GetProperty("balance").GetDecimal();

        finalBal.Should().Be(midBal + total, "Duplicate refund delivery must never inflate balance more than once");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F12")]
    public async Task TC_F12_B03_ZeroStartingBalance_RefundRestoresExactlyRefundAmount()
    {
        var user = "bnd_ref_3_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var (bookingId, paymentId, total, cardId) = await SetupPaidBookingAsync(token);

        // Set balance to 0.00
        await using (var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE demo_payment_methods SET balance = 0.00 WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", cardId);
            await cmd.ExecuteNonQueryAsync();
        }

        var eventId = Guid.NewGuid();
        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Bookings.V1",
            new PaymentRefundRequestedV1(
                PaymentId: paymentId,
                BookingId: bookingId,
                Reason: "Zero starting balance refund"
            ),
            "bookings.payment-refund-requested",
            eventId
        );

        var restored = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("balance").GetDecimal();
            },
            bal => bal == total,
            TimeSpan.FromSeconds(8)
        );

        restored.Should().Be(total, "Refund onto 0.00 balance must leave exactly the refunded amount");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F12")]
    public async Task TC_F12_B04_LedgerMovements_TracksSingleRefundRecord()
    {
        var user = "bnd_ref_4_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var (bookingId, paymentId, total, cardId) = await SetupPaidBookingAsync(token);

        var eventId = Guid.NewGuid();
        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Bookings.V1",
            new PaymentRefundRequestedV1(
                PaymentId: paymentId,
                BookingId: bookingId,
                Reason: "Single movement verification"
            ),
            "bookings.payment-refund-requested",
            eventId
        );

        await Task.Delay(2000);

        // Verify ledger movements table for this demo payment method
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM payment_ledger_movements WHERE demo_payment_method_id = @id AND operation_type = 'Refund';";
        cmd.Parameters.AddWithValue("@id", cardId);
        var refundCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        refundCount.Should().Be(1, "Exactly one refund ledger movement must be recorded");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F12")]
    public async Task TC_F12_B05_RefundTimestamp_WithinExpectedWindow()
    {
        var user = "bnd_ref_5_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var (bookingId, paymentId, total, cardId) = await SetupPaidBookingAsync(token);

        var eventId = Guid.NewGuid();
        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Bookings.V1",
            new PaymentRefundRequestedV1(
                PaymentId: paymentId,
                BookingId: bookingId,
                Reason: "Timestamp verification"
            ),
            "bookings.payment-refund-requested",
            eventId
        );

        var occurred = await PollingHelper.PollUntilAsync<DateTime?>(
            async () =>
            {
                await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT created_at_utc FROM payment_ledger_movements WHERE demo_payment_method_id = @id AND operation_type = 'Refund' ORDER BY id DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("@id", cardId);
                var res = await cmd.ExecuteScalarAsync();
                return res != null ? Convert.ToDateTime(res) : null;
            },
            dt => dt.HasValue,
            TimeSpan.FromSeconds(15)
        );

        occurred.Should().NotBeNull();
        occurred!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(2));
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
