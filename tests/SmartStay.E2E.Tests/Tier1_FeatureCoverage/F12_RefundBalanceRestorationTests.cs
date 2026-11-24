using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.Contracts.Events.V1;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F12")]
public class F12_RefundBalanceRestorationTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F12_RefundBalanceRestorationTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _activeMqFixture = new ActiveMqFixture();
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    private async Task<(int BookingId, int PaymentId, decimal Amount, string CardId, decimal InitialBalance, string Token)> CreatePaidBookingAsync(int testOffset)
    {
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, $"f12_{Guid.NewGuid():N}"[..18], "Password123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardJson = await cardResp.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = cardJson.GetProperty("id").GetString()!;
        var initialBalance = cardJson.GetProperty("balance").GetDecimal();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: testOffset + Random.Shared.Next(10, 5000), stayDuration: 2);
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "F12 Balance Restore",
            guestEmail = $"f12-{Guid.NewGuid():N}@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookJson = await bookResp.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = bookJson.GetProperty("id").GetInt32();
        var totalAmount = bookJson.GetProperty("totalAmount").GetDecimal();

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = totalAmount,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token, "pay-f12-" + Guid.NewGuid());
        payResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var payJson = await payResp.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = payJson.GetProperty("id").GetInt32();

        // Await confirmation
        var confirmed = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var bRes = await _gatewayClient.GetBookingByIdAsync(bookingId, token);
                if (!bRes.IsSuccessStatusCode) return false;
                var doc = JsonDocument.Parse(await bRes.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("status").GetString() == "Confirmed";
            },
            c => c,
            timeout: TimeSpan.FromSeconds(15)
        );
        confirmed.Should().BeTrue("Booking must be Confirmed before cancellation");

        return (bookingId, paymentId, totalAmount, cardId, initialBalance, token);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F12")]
    public async Task TC_F12_01_StandardPaidCancellation_RestoresBalanceAtomically()
    {
        var (bookingId, paymentId, totalAmount, cardId, initialBalance, token) = await CreatePaidBookingAsync(900);

        // Cancel the paid booking
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, token);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify balance is restored atomically on demo card via Payments refund processing
        var restored = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var cardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                if (!cardCheck.IsSuccessStatusCode) return false;
                var cardDoc = JsonDocument.Parse(await cardCheck.Content.ReadAsStringAsync());
                return cardDoc.RootElement.GetProperty("balance").GetDecimal() == initialBalance;
            },
            r => r,
            timeout: TimeSpan.FromSeconds(15)
        );

        restored.Should().BeTrue("Cancelling a paid booking must restore demo card balance atomically");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F12")]
    public async Task TC_F12_02_DuplicateRefundEvent_Idempotency()
    {
        var (bookingId, paymentId, totalAmount, cardId, initialBalance, token) = await CreatePaidBookingAsync(910);

        var eventId = Guid.NewGuid();
        var refundReq = new PaymentRefundRequestedV1(
            PaymentId: paymentId,
            BookingId: bookingId,
            Reason: "Duplicate Event Test"
        );

        // Send twice with same eventId
        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: refundReq,
            eventType: "bookings.payment-refund-requested",
            eventId: eventId
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: refundReq,
            eventType: "bookings.payment-refund-requested",
            eventId: eventId
        );

        // Await refund restoration
        var restored = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var cardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                if (!cardCheck.IsSuccessStatusCode) return false;
                var cardDoc = JsonDocument.Parse(await cardCheck.Content.ReadAsStringAsync());
                return cardDoc.RootElement.GetProperty("balance").GetDecimal() == initialBalance;
            },
            r => r,
            timeout: TimeSpan.FromSeconds(10)
        );

        restored.Should().BeTrue("Duplicate refund events must restore balance to initial credit");

        var finalCard = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBalance = (await finalCard.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
        finalBalance.Should().Be(initialBalance, "Balance must never be inflated by duplicate delivery");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F12")]
    public async Task TC_F12_03_RefundOnAlreadyRefundedPayment_StateIdempotency()
    {
        var (bookingId, paymentId, totalAmount, cardId, initialBalance, token) = await CreatePaidBookingAsync(920);

        // 1. First refund via booking cancellation
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, token);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        await PollingHelper.PollUntilAsync(
            async () =>
            {
                var cardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                if (!cardCheck.IsSuccessStatusCode) return false;
                var cardDoc = JsonDocument.Parse(await cardCheck.Content.ReadAsStringAsync());
                return cardDoc.RootElement.GetProperty("balance").GetDecimal() == initialBalance;
            },
            r => r,
            timeout: TimeSpan.FromSeconds(15)
        );

        // 2. Publish second refund request for same payment with fresh event ID
        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: new PaymentRefundRequestedV1(paymentId, bookingId, "Repeat Refund Second Attempt"),
            eventType: "bookings.payment-refund-requested",
            eventId: Guid.NewGuid()
        );

        await Task.Delay(1000);

        var cardAfter = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var balanceAfter = (await cardAfter.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
        balanceAfter.Should().Be(initialBalance, "Already refunded payments must be processed idempotently without double crediting");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F12")]
    public async Task TC_F12_04_UniqueConstraint_OnPaymentLedgerMovement_EnforcesSingleRefund()
    {
        var (bookingId, paymentId, totalAmount, cardId, initialBalance, token) = await CreatePaidBookingAsync(930);

        // Cancel booking to trigger refund
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, token);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        await PollingHelper.PollUntilAsync(
            async () =>
            {
                var cardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                if (!cardCheck.IsSuccessStatusCode) return false;
                var cardDoc = JsonDocument.Parse(await cardCheck.Content.ReadAsStringAsync());
                return cardDoc.RootElement.GetProperty("balance").GetDecimal() == initialBalance;
            },
            r => r,
            timeout: TimeSpan.FromSeconds(15)
        );

        // Replay refund request event
        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: new PaymentRefundRequestedV1(paymentId, bookingId, "Replay Unique Constraint Check"),
            eventType: "bookings.payment-refund-requested",
            eventId: Guid.NewGuid()
        );

        await Task.Delay(1000);

        // Verify in MySQL that payment_ledger_movements contains exactly 1 Refund movement for this payment
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM payment_ledger_movements WHERE payment_id = @pid AND operation_type = 'Refund'";
        cmd.Parameters.AddWithValue("@pid", paymentId);
        var refundCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        refundCount.Should().Be(1, "Ledger unique constraint must guarantee exactly one refund ledger movement per payment");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F12")]
    public async Task TC_F12_05_PostRefundDemoCard_AlgebraicBalanceConsistency()
    {
        var (bookingId, paymentId, totalAmount, cardId, initialBalance, token) = await CreatePaidBookingAsync(940);

        // 1. Check debited balance before cancellation
        var cardMid = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var midBal = (await cardMid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
        midBal.Should().Be(initialBalance - totalAmount, "Balance immediately after payment must reflect exact debit");

        // 2. Cancel booking to trigger refund
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, token);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Await restoration
        var restored = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var cardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                if (!cardCheck.IsSuccessStatusCode) return false;
                var cardDoc = JsonDocument.Parse(await cardCheck.Content.ReadAsStringAsync());
                return cardDoc.RootElement.GetProperty("balance").GetDecimal() == initialBalance;
            },
            r => r,
            timeout: TimeSpan.FromSeconds(15)
        );

        restored.Should().BeTrue("Balance must be restored to initial balance after cancellation");

        var cardFinal = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBal = (await cardFinal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();

        // Exact algebraic consistency: finalBal - midBal must equal totalAmount exactly
        (finalBal - midBal).Should().Be(totalAmount, "Restored balance minus debited balance must equal totalAmount down to the cent");
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
    }
}
