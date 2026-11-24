using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F10")]
public class F10_PaymentLedgerAtomicTransactionBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F10_PaymentLedgerAtomicTransactionBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    private async Task<(int BookingId, decimal TotalAmount, string DemoMethodId)> SetupBookingAndCardAsync(string token)
    {
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync());
        var demoMethodId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            roomId = 101,
            guestName = "Ledger Guest",
            guestEmail = "ledger@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var bookResp = await _gatewayClient.CreateBookingAsync(payload, token);
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created, "Setup booking must succeed before extracting bookingId");
        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal();

        return (bookingId, total, demoMethodId);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F10")]
    public async Task TC_F10_B01_ZeroAmountPayment_ReturnsBadRequest()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (bookingId, _, demoMethodId) = await SetupBookingAndCardAsync(token);

        var payload = new
        {
            bookingId,
            amount = 0.00m,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = demoMethodId
        };

        var response = await _gatewayClient.ProcessPaymentAsync(payload, token);
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity },
            "Payment with zero amount must be rejected"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F10")]
    public async Task TC_F10_B02_NegativeAmountPayment_ReturnsBadRequest()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (bookingId, _, demoMethodId) = await SetupBookingAndCardAsync(token);

        var payload = new
        {
            bookingId,
            amount = -50.00m,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = demoMethodId
        };

        var response = await _gatewayClient.ProcessPaymentAsync(payload, token);
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity },
            "Payment with negative amount must be rejected"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F10")]
    public async Task TC_F10_B03_IdempotencyKeyReuseWithDifferentAmount_ReturnsConflict()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (bookingId, total, demoMethodId) = await SetupBookingAndCardAsync(token);

        var key = "idem_bnd_" + Guid.NewGuid().ToString("N");
        var payload1 = new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = demoMethodId
        };

        var resp1 = await _gatewayClient.ProcessPaymentAsync(payload1, token, idempotencyKey: key);
        resp1.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // Reusing same idempotency key with different amount
        var payload2 = new
        {
            bookingId,
            amount = total + 50.00m,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = demoMethodId
        };

        var resp2 = await _gatewayClient.ProcessPaymentAsync(payload2, token, idempotencyKey: key);
        resp2.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest },
            "Reusing an idempotency key with a mutated payload must return 409 Conflict or 400 Bad Request"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F10")]
    public async Task TC_F10_B04_ExtremeLengthIdempotencyKey_HandledCleanly()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (bookingId, total, demoMethodId) = await SetupBookingAndCardAsync(token);

        // Exactly 100-char unique idempotency key (maximum capacity of varchar(100) column)
        var prefix = "k_" + Guid.NewGuid().ToString("N");
        var maxCapacityKey = prefix + new string('A', 100 - prefix.Length);
        var payload = new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = demoMethodId
        };

        var response = await _gatewayClient.ProcessPaymentAsync(payload, token, idempotencyKey: maxCapacityKey);
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.OK, HttpStatusCode.Created },
            "Idempotency key of exactly 100 characters (max column boundary) must be processed cleanly"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F10")]
    public async Task TC_F10_B05_UniqueLedgerMovementConstraint()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        // Check unique index on payment_ledger_movements
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT index_name, column_name 
            FROM information_schema.statistics 
            WHERE table_schema = DATABASE() 
              AND table_name = 'payment_ledger_movements'
              AND non_unique = 0;";

        var uniqueIndexes = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            uniqueIndexes.Add(reader.GetString(0));
        }

        uniqueIndexes.Should().NotBeEmpty("payment_ledger_movements must enforce unique constraints on payment movements");
    }
}
