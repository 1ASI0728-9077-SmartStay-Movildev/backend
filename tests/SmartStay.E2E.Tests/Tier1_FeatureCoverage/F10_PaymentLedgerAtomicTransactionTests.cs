using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F10")]
public class F10_PaymentLedgerAtomicTransactionTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F10_PaymentLedgerAtomicTransactionTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F10")]
    public async Task TC_F10_01_DemoPayment_SingleAtomicTransactionProcessing()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = (await cardResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        // Create booking to pay
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(70);
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "F10 Atomic Payment",
            guestEmail = "f10-pay@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookJson = await bookResp.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = bookJson.GetProperty("id").GetInt32();
        var totalAmount = bookJson.GetProperty("totalAmount").GetDecimal();

        var payload = new
        {
            bookingId,
            amount = totalAmount,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        };

        var response = await _gatewayClient.ProcessPaymentAsync(payload, token, "pay-f10-01-" + Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.Created, "Valid demo payment processing must return 201 Created");

        var payment = await response.Content.ReadFromJsonAsync<JsonElement>();
        payment.GetProperty("status").GetString().Should().Be("Completed");
        payment.GetProperty("amount").GetDecimal().Should().Be(totalAmount);
        payment.GetProperty("transactionId").GetString().Should().NotBeNullOrWhiteSpace();
        payment.GetProperty("receiptVoucher").GetString().Should().StartWith("VCH-");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F10")]
    public async Task TC_F10_02_IdempotentPaymentReplay_WithSameKey()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = (await cardResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(75);
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "F10 Replay Test",
            guestEmail = "f10-replay@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookJson = await bookResp.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = bookJson.GetProperty("id").GetInt32();
        var totalAmount = bookJson.GetProperty("totalAmount").GetDecimal();

        var idempotencyKey = "pay-f10-replay-" + Guid.NewGuid();
        var payload = new
        {
            bookingId,
            amount = totalAmount,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        };

        // First attempt -> 201 Created
        var resp1 = await _gatewayClient.ProcessPaymentAsync(payload, token, idempotencyKey);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();

        // Replay attempt -> 200 OK with identical payment ID and voucher
        var resp2 = await _gatewayClient.ProcessPaymentAsync(payload, token, idempotencyKey);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK, "Idempotent payment replay must return 200 OK");
        var payment2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();

        payment2.GetProperty("id").GetInt32().Should().Be(payment1.GetProperty("id").GetInt32());
        payment2.GetProperty("transactionId").GetString().Should().Be(payment1.GetProperty("transactionId").GetString());
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F10")]
    public async Task TC_F10_03_UniqueMovementConstraint_Enforcement()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardBefore = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var cardBeforeJson = await cardBefore.Content.ReadFromJsonAsync<JsonElement>();
        var balanceBefore = cardBeforeJson.GetProperty("balance").GetDecimal();
        var cardId = cardBeforeJson.GetProperty("id").GetString();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(80);
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "F10 Single Movement",
            guestEmail = "f10-singlemv@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookJson = await bookResp.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = bookJson.GetProperty("id").GetInt32();
        var totalAmount = bookJson.GetProperty("totalAmount").GetDecimal();

        var idempotencyKey = "pay-f10-singlemv-" + Guid.NewGuid();
        var payload = new
        {
            bookingId,
            amount = totalAmount,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        };

        // First attempt -> 201 Created
        var resp1 = await _gatewayClient.ProcessPaymentAsync(payload, token, idempotencyKey);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Replay attempt -> 200 OK
        var resp2 = await _gatewayClient.ProcessPaymentAsync(payload, token, idempotencyKey);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);

        var cardAfter = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var balanceAfter = (await cardAfter.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();

        (balanceBefore - balanceAfter).Should().Be(totalAmount, "Replaying a payment must not insert a duplicate ledger debit movement or debit balance twice");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F10")]
    public async Task TC_F10_04_IdempotencyKeyPayloadMismatch_Rejection()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var cardId = (await cardResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(85);
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "F10 Mismatch Test",
            guestEmail = "f10-mismatch@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookJson = await bookResp.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = bookJson.GetProperty("id").GetInt32();
        var totalAmount = bookJson.GetProperty("totalAmount").GetDecimal();

        var sharedKey = "pay-f10-shared-" + Guid.NewGuid();
        var payload1 = new
        {
            bookingId,
            amount = totalAmount,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        };
        var payload2Modified = new
        {
            bookingId,
            amount = totalAmount + 50.00m, // Modified amount with same idempotency key
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        };

        var resp1 = await _gatewayClient.ProcessPaymentAsync(payload1, token, sharedKey);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);

        var resp2 = await _gatewayClient.ProcessPaymentAsync(payload2Modified, token, sharedKey);
        resp2.StatusCode.Should().Be(HttpStatusCode.Conflict, "Reusing an idempotency key with different payload must return 409 Conflict");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F10")]
    public async Task TC_F10_05_UnauthorizedPayment_UsingForeignDemoCard_Rejected()
    {
        var guest1Token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var card1Resp = await _gatewayClient.GetDemoPaymentMethodAsync(guest1Token);
        var card1Id = (await card1Resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var guest2Token = await _authFixture.GetGuest2TokenAsync(_apiFixture.GatewayClient);

        // Guest 2 creates their own booking
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(90);
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "Guest2 Booking",
            guestEmail = "g2-own@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, guest2Token);
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookJson = await bookResp.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = bookJson.GetProperty("id").GetInt32();
        var totalAmount = bookJson.GetProperty("totalAmount").GetDecimal();

        var foreignCardPayload = new
        {
            bookingId,
            amount = totalAmount,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = card1Id // Belonging to Guest 1
        };

        var response = await _gatewayClient.ProcessPaymentAsync(foreignCardPayload, guest2Token);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "Paying with a demo card belonging to another guest must be rejected with 409 Conflict");
    }
}
