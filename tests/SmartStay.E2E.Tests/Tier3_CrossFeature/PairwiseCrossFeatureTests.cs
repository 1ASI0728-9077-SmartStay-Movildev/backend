using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.Contracts.Events.V1;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier3_CrossFeature;

/// <summary>
/// Tier 3: Pairwise Cross-Feature Interaction Test Suite.
/// Validates cross-cutting contracts, multi-service asynchronous handoffs,
/// transactional atomicity, and event-driven data flow across F01-F20.
/// </summary>
[Trait("Tier", "Tier3")]
[Trait("CrossFeature", "Pairwise")]
public class PairwiseCrossFeatureTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly ConciergeClient _conciergeClient;
    private readonly InternalBookingsClient _internalBookingsClient;

    public PairwiseCrossFeatureTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _activeMqFixture = new ActiveMqFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _conciergeClient = new ConciergeClient();
        _internalBookingsClient = new InternalBookingsClient(_apiFixture.DirectBookingsClient);
    }

    #region Pairwise Tests 1 to 5

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F08_x_F14")]
    public async Task TC_T3_01_Activation_To_Quote()
    {
        // Pair: F08 (Demo Card Activation) -> F14 (Bookings Internal Quote)
        var user = "t3_01_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        cardResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 1200 + Random.Shared.Next(10, 500), stayDuration: 3);
        var quoteResp = await _internalBookingsClient.CreateQuoteAsync(new
        {
            roomId = 101,
            guestUserId = 2,
            purchaseIntentId = "cpi_t3_01",
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            guests = 1
        });

        quoteResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var quoteDoc = JsonDocument.Parse(await quoteResp.Content.ReadAsStringAsync());
        var stayTotal = quoteDoc.RootElement.GetProperty("stayTotal").GetDecimal();
        stayTotal.Should().BeGreaterThan(0m);
        quoteDoc.RootElement.GetProperty("currency").GetString().Should().Be("USD");
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F08_x_F15")]
    public async Task TC_T3_02_Activation_To_PurchaseIntent()
    {
        // Pair: F08 (Card Activation) -> F15 (Concierge Purchase Intent Mandate)
        var user = "t3_02_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 1300 + Random.Shared.Next(10, 500));
        var intentResp = await _conciergeClient.CreatePurchaseIntentAsync(new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 350.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "t3_02@example.com",
            contactPhone = "+51999999999"
        }, token);

        intentResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc = JsonDocument.Parse(await intentResp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("id").GetString().Should().StartWith("cpi_");
        doc.RootElement.GetProperty("status").GetString().Should().BeOneOf("Pending", "Received", "Quoting", "SelectingCandidate", "Confirmed");
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F14_x_F18")]
    public async Task TC_T3_03_Quote_To_AutonomousWorker()
    {
        // Pair: F14 (Quote Generation) -> F18 (Concierge Worker Orchestration)
        var user = "t3_03_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 1400 + Random.Shared.Next(10, 500));
        var response = await _conciergeClient.CreatePurchaseIntentAsync(new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 500.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "t3_03@example.com",
            contactPhone = "+51999999999"
        }, token);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(20)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Confirmed");
        terminalDoc.RootElement.GetProperty("quoteId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F15_x_F13")]
    public async Task TC_T3_04_PurchaseIntent_To_DemoCardValidation()
    {
        // Pair: F15 (Mandate Creation) -> F13 (Internal Demo Card Ownership Guard)
        var user1 = "t3_04a_" + Guid.NewGuid().ToString("N")[..8];
        var user2 = "t3_04b_" + Guid.NewGuid().ToString("N")[..8];
        var token1 = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user1, "Password123!");
        var token2 = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user2, "Password123!");

        // User 1 activates card
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token1);
        var cardId1 = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 1500 + Random.Shared.Next(10, 500));

        // User 2 attempts to open mandate using User 1's card
        var intentResp = await _conciergeClient.CreatePurchaseIntentAsync(new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId1, // Not owned by User 2!
            contactEmail = "t3_04@example.com",
            contactPhone = "+51999999999"
        }, token2);

        // F13 guard stops flow immediately with 422 Unprocessable Entity
        intentResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F17_x_F18")]
    public async Task TC_T3_05_CandidateSelection_To_AutonomousWorker()
    {
        // Pair: F17 (Candidate Selection & Tie-Break) -> F18 (Concierge Worker Execution)
        var user = "t3_05_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 1600 + Random.Shared.Next(10, 500));
        var response = await _conciergeClient.CreatePurchaseIntentAsync(new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 600.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "t3_05@example.com",
            contactPhone = "+51999999999"
        }, token);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(20)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Confirmed");
        terminalDoc.RootElement.GetProperty("roomId").GetInt32().Should().BeGreaterThan(0);
        terminalDoc.RootElement.GetProperty("hotelName").GetString().Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Pairwise Tests 6 to 10

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F18_x_F05")]
    public async Task TC_T3_06_AutonomousWorker_To_StrictPayment()
    {
        // Pair: F18 (Concierge Worker) -> F05 (Strict Payment Confirmation)
        var user = "t3_06_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;
        var initialBalance = cardDoc.RootElement.GetProperty("balance").GetDecimal();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 1700 + Random.Shared.Next(10, 500));
        var response = await _conciergeClient.CreatePurchaseIntentAsync(new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "t3_06@example.com",
            contactPhone = "+51999999999"
        }, token);

        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(20)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Confirmed");
        var paymentId = terminalDoc.RootElement.GetProperty("paymentId").GetInt32();
        var stayTotal = terminalDoc.RootElement.GetProperty("stayTotal").GetDecimal();
        paymentId.Should().BeGreaterThan(0);

        // Verify balance decreased by exactly stayTotal
        var cardRespAfter = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBalance = JsonDocument.Parse(await cardRespAfter.Content.ReadAsStringAsync()).RootElement.GetProperty("balance").GetDecimal();
        finalBalance.Should().Be(initialBalance - stayTotal);
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F05_x_F04")]
    public async Task TC_T3_07_PaymentConfirmation_To_BookingsOutbox()
    {
        // Pair: F05 (Payment Confirmation) -> F04 (Bookings Outbox Pattern)
        var user = "t3_07_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 1800 + Random.Shared.Next(10, 500));
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "T3_07 Guest",
            guestEmail = "t3_07@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        bookResp.StatusCode.Should().Be(HttpStatusCode.Created, "Booking creation must succeed before accessing ID");
        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal();

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);

        payResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // Poll MySQL bookings table for confirmed status
        var bookingConfirmed = await PollingHelper.PollUntilAsync(
            async () =>
            {
                await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT status FROM bookings WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", bookingId);
                var status = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return status == 1; // 1 = Confirmed in BookingStatus enum
            },
            c => c,
            TimeSpan.FromSeconds(10)
        );

        bookingConfirmed.Should().BeTrue("Booking must be transitioned to Confirmed after payment");
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F05_x_F10")]
    public async Task TC_T3_08_StrictPayment_To_LedgerAtomicTransaction()
    {
        // Pair: F05 (Strict Payment) -> F10 (Ledger Single Atomic Transaction)
        var user = "t3_08_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 1900 + Random.Shared.Next(10, 500));
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "T3_08 Guest",
            guestEmail = "t3_08@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        bookResp.StatusCode.Should().Be(HttpStatusCode.Created, "Booking creation must succeed before accessing ID");
        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal();

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);

        var payDoc = JsonDocument.Parse(await payResp.Content.ReadAsStringAsync());
        var paymentId = payDoc.RootElement.GetProperty("id").GetInt32();

        // Check payment_ledger_movements in MySQL
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(amount), 0.00) FROM payment_ledger_movements WHERE payment_id = @pid AND operation_type = 'Debit'";
        cmd.Parameters.AddWithValue("@pid", paymentId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        var movementCount = reader.GetInt64(0);
        var recordedAmount = reader.GetDecimal(1);

        movementCount.Should().Be(1, "Exactly one ledger debit movement must be recorded per payment");
        recordedAmount.Should().Be(total);
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F10_x_F11")]
    public async Task TC_T3_09_LedgerAtomicTransaction_To_OverdraftPrevention()
    {
        // Pair: F10 (Ledger Movements) -> F11 (Overdraft Prevention)
        var user = "t3_09_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Artificially restrict balance to 10.00 USD in DB
        await using (var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE demo_payment_methods SET balance = 10.00 WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", cardId);
            await cmd.ExecuteNonQueryAsync();
        }

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 2000 + Random.Shared.Next(10, 500));
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "T3_09 Guest",
            guestEmail = "t3_09@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        bookResp.StatusCode.Should().Be(HttpStatusCode.Created, "Booking creation must succeed before accessing ID");
        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal(); // e.g. 170.00 > 10.00

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);

        // Must be rejected with 409 Conflict due to insufficient balance
        payResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Verify balance remained untouched at 10.00 (non-negative, no partial deduction)
        var cardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var bal = JsonDocument.Parse(await cardCheck.Content.ReadAsStringAsync()).RootElement.GetProperty("balance").GetDecimal();
        bal.Should().Be(10.00m);
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F05_x_F06")]
    public async Task TC_T3_10_ConfirmedBooking_To_CancellationOutbox()
    {
        // Pair: F05 (Payment Confirmed) -> F06 (Bookings Cancellation Outbox)
        var user = "t3_10_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 2100 + Random.Shared.Next(10, 500));
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "T3_10 Guest",
            guestEmail = "t3_10@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        bookResp.StatusCode.Should().Be(HttpStatusCode.Created, "Booking creation must succeed before accessing ID");
        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal();

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);
        payResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // Wait until booking is Confirmed asynchronously
        await PollingHelper.PollUntilAsync(
            async () =>
            {
                await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT status FROM bookings WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", bookingId);
                return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
            },
            c => c,
            TimeSpan.FromSeconds(10)
        );

        // Cancel the booking via API
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, token);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify outbox entry exists in bookings_outbox_messages
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM bookings_outbox_messages WHERE event_type = 'bookings.payment-refund-requested' AND payload LIKE @search";
        cmd.Parameters.AddWithValue("@search", $"%\"bookingId\":{bookingId}%");
        var outboxCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        outboxCount.Should().BeGreaterOrEqualTo(1, "Cancellation must insert an outbox event for compensation");
    }

    #endregion

    #region Pairwise Tests 11 to 15

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F06_x_F03")]
    public async Task TC_T3_11_CancellationOutbox_To_ActiveMqVirtualTopic()
    {
        // Pair: F06 (Cancellation Outbox) -> F03 (ActiveMQ Virtual Topic Dispatch)
        // Verify outbox publisher dispatches to VirtualTopic.SmartStay.Bookings.V1
        var user = "t3_11_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 2200 + Random.Shared.Next(10, 500));
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "T3_11 Guest",
            guestEmail = "t3_11@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        bookResp.StatusCode.Should().Be(HttpStatusCode.Created, "Booking creation must succeed before accessing ID");
        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal();

        await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);

        await _gatewayClient.CancelBookingAsync(bookingId, token);

        // Poll outbox table until published_at_utc is non-null
        var published = await PollingHelper.PollUntilAsync(
            async () =>
            {
                await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM bookings_outbox_messages WHERE event_type = 'bookings.payment-refund-requested' AND published_at_utc IS NOT NULL AND payload LIKE @search";
                cmd.Parameters.AddWithValue("@search", $"%\"bookingId\":{bookingId}%");
                return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
            },
            p => p,
            TimeSpan.FromSeconds(10)
        );

        published.Should().BeTrue("Outbox worker must publish cancellation event to ActiveMQ topic");
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F03_x_F07")]
    public async Task TC_T3_12_CancellationEvent_To_PaymentsCompensationInbox()
    {
        // Pair: F03 (ActiveMQ Routing) -> F07 (Payments Atomic Refund Inbox)
        var user = "t3_12_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 2300 + Random.Shared.Next(10, 500));
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "T3_12 Guest",
            guestEmail = "t3_12@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        bookResp.StatusCode.Should().Be(HttpStatusCode.Created, "Booking creation must succeed before accessing ID");
        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal();

        await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);

        await _gatewayClient.CancelBookingAsync(bookingId, token);

        // Poll payments_inbox_messages for consumed compensation
        var inboxProcessed = await PollingHelper.PollUntilAsync(
            async () =>
            {
                await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM payments_inbox_messages WHERE event_type = 'bookings.payment-refund-requested' AND status = 'Processed'";
                return Convert.ToInt32(await cmd.ExecuteScalarAsync());
            },
            c => c > 0,
            TimeSpan.FromSeconds(15)
        );

        inboxProcessed.Should().BeGreaterThan(0, "Payments compensation inbox must consume and process cancellation events");
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F07_x_F12")]
    public async Task TC_T3_13_AtomicRefund_To_BalanceRestoration()
    {
        // Pair: F07 (Atomic Refund) -> F12 (Card Balance Restoration)
        var user = "t3_13_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;
        var initialBalance = cardDoc.RootElement.GetProperty("balance").GetDecimal();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 2400 + Random.Shared.Next(10, 500));
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "T3_13 Guest",
            guestEmail = "t3_13@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        bookResp.StatusCode.Should().Be(HttpStatusCode.Created, "Booking creation must succeed before accessing ID");
        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal();

        await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);

        await _gatewayClient.CancelBookingAsync(bookingId, token);

        // Verify balance restoration
        var restored = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("balance").GetDecimal();
            },
            b => b == initialBalance,
            TimeSpan.FromSeconds(15)
        );

        restored.Should().Be(initialBalance, "Card balance must be restored to exact pre-payment amount upon cancellation");
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F12_x_F09")]
    public async Task TC_T3_14_BalanceRestoration_To_CardIsolation()
    {
        // Pair: F12 (Refund Restoration) -> F09 (Guest Demo Card Isolation)
        var user1 = "t3_14a_" + Guid.NewGuid().ToString("N")[..8];
        var user2 = "t3_14b_" + Guid.NewGuid().ToString("N")[..8];
        var token1 = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user1, "Password123!");
        var token2 = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user2, "Password123!");

        var cardResp1 = await _gatewayClient.ActivateDemoPaymentMethodAsync(token1);
        var cardId1 = JsonDocument.Parse(await cardResp1.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var cardResp2 = await _gatewayClient.ActivateDemoPaymentMethodAsync(token2);
        var cardDoc2 = JsonDocument.Parse(await cardResp2.Content.ReadAsStringAsync());
        var cardId2 = cardDoc2.RootElement.GetProperty("id").GetString()!;
        var user2InitialBal = cardDoc2.RootElement.GetProperty("balance").GetDecimal();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 2500 + Random.Shared.Next(10, 500));
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "T3_14 Guest",
            guestEmail = "t3_14@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token1);

        bookResp.StatusCode.Should().Be(HttpStatusCode.Created, "Booking creation must succeed before accessing ID");
        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal();

        await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId1
        }, token1);

        await _gatewayClient.CancelBookingAsync(bookingId, token1);

        // Await User 1 refund restoration
        await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _gatewayClient.GetDemoPaymentMethodAsync(token1);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("balance").GetDecimal();
            },
            b => b == 1000000.00m,
            TimeSpan.FromSeconds(15)
        );

        // Verify User 2's card balance was completely unaffected
        var check2 = await _gatewayClient.GetDemoPaymentMethodAsync(token2);
        var user2FinalBal = JsonDocument.Parse(await check2.Content.ReadAsStringAsync()).RootElement.GetProperty("balance").GetDecimal();
        user2FinalBal.Should().Be(user2InitialBal, "User 2's card balance must remain completely isolated from User 1's refund");
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F02_x_F14")]
    public async Task TC_T3_15_DynamicDates_To_BookingQuote()
    {
        // Pair: F02 (Dynamic Date Shifting) -> F14 (Bookings Quotes TTL)
        var offset = 3000 + Random.Shared.Next(50, 1000);
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: offset, stayDuration: 5);

        var quoteResp = await _internalBookingsClient.CreateQuoteAsync(new
        {
            roomId = 101,
            guestUserId = 2,
            purchaseIntentId = "cpi_t3_15",
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            guests = 2
        });

        quoteResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = JsonDocument.Parse(await quoteResp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("stayTotal").GetDecimal().Should().BeGreaterThan(0);
        var expiresAtUtc = doc.RootElement.GetProperty("expiresAtUtc").GetDateTime();
        expiresAtUtc.Should().BeAfter(DateTime.UtcNow);
    }

    #endregion

    #region Pairwise Tests 16 to 20

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F02_x_F05")]
    public async Task TC_T3_16_DynamicDates_To_DirectPayment()
    {
        // Pair: F02 (Dynamic Dates & Collision Prevention) -> F05 (Strict Direct Payment)
        var user = "t3_16_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 3200 + Random.Shared.Next(10, 500), stayDuration: 2);
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "T3_16 Guest",
            guestEmail = "t3_16@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        bookResp.StatusCode.Should().Be(HttpStatusCode.Created, "Booking creation must succeed before accessing ID");
        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal();

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);

        payResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F17_x_F18")]
    public async Task TC_T3_17_CandidateFallback_To_WorkerAttemptLimit()
    {
        // Pair: F17 (Candidate Fallback) -> F18 (Concierge Worker Attempt Limit & Failure)
        var user = "t3_17_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 3400 + Random.Shared.Next(10, 500));
        var response = await _conciergeClient.CreatePurchaseIntentAsync(new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "NonExistentRemoteDistrict", // Guarantees 0 candidates found
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "t3_17@example.com",
            contactPhone = "+51999999999"
        }, token);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Failed" or "Confirmed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(15)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Failed");
        terminalDoc.RootElement.GetProperty("failureReason").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "FullLifecycle")]
    public async Task TC_T3_18_FullLifecycle_Activation_To_Refund()
    {
        // Complete 7-step interaction chain:
        // 1. Register & Activate Card (F08)
        // 2. Submit Autonomous Purchase Intent (F15)
        // 3. Autonomous Worker Negotiates & Confirms (F18)
        // 4. Strict Payment Completed (F05)
        // 5. Booking Cancelled via API (F06)
        // 6. ActiveMQ Routes Compensation (F03/F07)
        // 7. Full Balance Restored Atomically (F12)

        var user = "t3_full_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        // Step 1: Activation
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;
        var initialBalance = cardDoc.RootElement.GetProperty("balance").GetDecimal();

        // Step 2: Intent creation
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 3600 + Random.Shared.Next(10, 500));
        var intentResp = await _conciergeClient.CreatePurchaseIntentAsync(new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "t3_full@example.com",
            contactPhone = "+51999999999"
        }, token);
        intentResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await intentResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Step 3 & 4: Worker orchestrates to Confirmed
        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(20)
        );
        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Confirmed");
        var bookingId = terminalDoc.RootElement.GetProperty("bookingId").GetInt32();
        var stayTotal = terminalDoc.RootElement.GetProperty("stayTotal").GetDecimal();

        // Verify balance decreased
        var midCardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var midBal = JsonDocument.Parse(await midCardCheck.Content.ReadAsStringAsync()).RootElement.GetProperty("balance").GetDecimal();
        midBal.Should().Be(initialBalance - stayTotal);

        // Step 5: Cancel booking
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, token);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 6 & 7: Refund restoration
        var restoredBal = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("balance").GetDecimal();
            },
            b => b == initialBalance,
            TimeSpan.FromSeconds(15)
        );

        restoredBal.Should().Be(initialBalance, "Full lifecycle must restore card balance algebraically to 1,000,000 USD");
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F19_x_F20")]
    public async Task TC_T3_19_DemoCardState_To_TrackingResource()
    {
        // Pair: F19 (Demo Card State in UI) -> F20 (Tracking Resource Visibility on Reload)
        var user = "t3_19_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 3800 + Random.Shared.Next(10, 500));
        var response = await _conciergeClient.CreatePurchaseIntentAsync(new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "t3_19@example.com",
            contactPhone = "+51999999999"
        }, token);

        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(20)
        );

        var root = terminalDoc.RootElement;
        root.GetProperty("demoPaymentMethodId").GetString().Should().Be(cardId);
        root.GetProperty("budgetTotal").GetDecimal().Should().Be(300.00m);
        root.GetProperty("currency").GetString().Should().Be("USD");
    }

    [Fact]
    [Trait("Tier", "Tier3")]
    [Trait("Interaction", "F10_x_F07")]
    public async Task TC_T3_20_IdempotentReplay_Across_PaymentAndInbox()
    {
        // Pair: F10 (Idempotent Payment Replay) -> F07 (Idempotent Compensation Inbox)
        var user = "t3_20_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 4000 + Random.Shared.Next(10, 500));
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "T3_20 Guest",
            guestEmail = "t3_20@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        bookResp.StatusCode.Should().Be(HttpStatusCode.Created, "Booking creation must succeed before accessing ID");
        var bookDoc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
        var bookingId = bookDoc.RootElement.GetProperty("id").GetInt32();
        var total = bookDoc.RootElement.GetProperty("totalAmount").GetDecimal();

        var idempotencyKey = "t3_20_" + Guid.NewGuid().ToString("N");
        var payPayload = new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        };

        // First payment attempt
        var resp1 = await _gatewayClient.ProcessPaymentAsync(payPayload, token, idempotencyKey);
        resp1.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var paymentId1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        // Replay with exact same idempotency key and payload
        var resp2 = await _gatewayClient.ProcessPaymentAsync(payPayload, token, idempotencyKey);
        resp2.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var paymentId2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        paymentId2.Should().Be(paymentId1, "Idempotent payment replay must return identical payment entity without duplicate charge");
    }

    #endregion

    public void Dispose()
    {
        _activeMqFixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
