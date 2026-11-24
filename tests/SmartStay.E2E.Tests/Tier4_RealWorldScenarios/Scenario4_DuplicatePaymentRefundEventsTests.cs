using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.Contracts.Events.V1;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier4_RealWorldScenarios;

/// <summary>
/// Tier 4 - Scenario 4: Duplicate Payment & Refund Events.
/// Tests end-to-end idempotency at HTTP Gateway level and Inbox Deduplication at ActiveMQ level.
/// Guarantees that duplicate payment replays and duplicate ActiveMQ compensation events are idempotent.
/// </summary>
[Trait("Tier", "Tier4")]
[Trait("Scenario", "Scenario4_DuplicatePaymentRefundEvents")]
public class Scenario4_DuplicatePaymentRefundEventsTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly GatewayClient _gatewayClient;

    public Scenario4_DuplicatePaymentRefundEventsTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _activeMqFixture = new ActiveMqFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier4")]
    public async Task Scenario4_Part1_ConcurrentDuplicatePaymentRequests_IdempotentSinglePayment()
    {
        const int concurrentCalls = 10;
        var user = "idemp_pay_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "IdempPass123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;
        var initialBalance = cardDoc.RootElement.GetProperty("balance").GetDecimal();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 9000 + Random.Shared.Next(10, 500), stayDuration: 2);
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "Idempotent Guest",
            guestEmail = "idemp@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        var bookingId = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
        var total = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync()).RootElement.GetProperty("totalAmount").GetDecimal();

        var sharedIdempotencyKey = "idemp_batch_" + Guid.NewGuid().ToString("N");
        var payPayload = new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        };

        // Fire 10 concurrent requests with identical idempotency key and identical payload
        using var barrier = new ManualResetEventSlim(false);
        var responses = new ConcurrentBag<(HttpStatusCode Status, int PaymentId)>();

        var tasks = Enumerable.Range(0, concurrentCalls).Select(async _ =>
        {
            await Task.Yield();
            barrier.Wait();

            var resp = await _gatewayClient.ProcessPaymentAsync(payPayload, token, sharedIdempotencyKey);
            if (resp.IsSuccessStatusCode)
            {
                var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var pid = doc.RootElement.GetProperty("id").GetInt32();
                responses.Add((resp.StatusCode, pid));
            }
        }).ToArray();

        barrier.Set();
        await Task.WhenAll(tasks);

        responses.Should().NotBeEmpty("At least the winning and idempotent replay calls must succeed");
        var distinctPaymentIds = responses.Select(r => r.PaymentId).Distinct().ToList();
        distinctPaymentIds.Should().ContainSingle("All successful calls under the same idempotency key must return the exact same payment ID");

        // Verify balance was debited ONLY ONCE
        var cardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBal = JsonDocument.Parse(await cardCheck.Content.ReadAsStringAsync()).RootElement.GetProperty("balance").GetDecimal();
        finalBal.Should().Be(initialBalance - total, "Card balance must be deducted exactly once despite 10 concurrent replays");

        // Verify ledger movements table in MySQL
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM payment_ledger_movements WHERE demo_payment_method_id = @id AND operation_type = 'Debit'";
        cmd.Parameters.AddWithValue("@id", cardId);
        var debitCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        debitCount.Should().Be(1, "Exactly one debit movement must be recorded in payment_ledger_movements");
    }

    [Fact]
    [Trait("Tier", "Tier4")]
    public async Task Scenario4_Part2_DuplicateActiveMqCompensationEvents_InboxDeduplication()
    {
        var user = "inbox_dedup_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "InboxPass123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;
        var initialBalance = cardDoc.RootElement.GetProperty("balance").GetDecimal();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 9200 + Random.Shared.Next(10, 500), stayDuration: 2);
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "Inbox Guest",
            guestEmail = "inbox@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);

        var bookingId = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
        var total = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync()).RootElement.GetProperty("totalAmount").GetDecimal();

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);

        var paymentId = JsonDocument.Parse(await payResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

        // Broadcast 3 identical ActiveMQ compensation messages with the EXACT same EventId
        var duplicateEventId = Guid.NewGuid();
        var refundEvt = new PaymentRefundRequestedV1(
            PaymentId: paymentId,
            BookingId: bookingId,
            Reason: "Duplicate ActiveMQ delivery test"
        );

        for (var i = 0; i < 3; i++)
        {
            _activeMqFixture.PublishIntegrationEvent(
                "VirtualTopic.SmartStay.Bookings.V1",
                refundEvt,
                "bookings.payment-refund-requested",
                duplicateEventId
            );
        }

        // Await balance restoration
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

        restoredBal.Should().Be(initialBalance, "Balance must be restored exactly once back to initial balance");

        // Verify payments_inbox_messages and payment_ledger_movements in MySQL
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        // Exactly one inbox message processed for this EventId
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM payments_inbox_messages WHERE event_id = @eid";
            cmd.Parameters.AddWithValue("@eid", duplicateEventId.ToString());
            var inboxCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            inboxCount.Should().Be(1, "Payments inbox must store exactly 1 processed record for duplicate event ID");
        }

        // Exactly one refund movement recorded in ledger
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM payment_ledger_movements WHERE payment_id = @pid AND operation_type = 'Refund'";
            cmd.Parameters.AddWithValue("@pid", paymentId);
            var refundCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            refundCount.Should().Be(1, "Exactly 1 refund movement must be recorded in ledger (no balance inflation)");
        }
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
