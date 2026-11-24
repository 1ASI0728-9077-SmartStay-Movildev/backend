using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier4_RealWorldScenarios;

/// <summary>
/// Tier 4 - Scenario 2: High Concurrency Contention.
/// Fires 20 simultaneous booking requests for the exact same room and date window across a thread barrier.
/// Asserts strict physical lock acquisition: exactly 1 winner (201), 19 rejections (409/422), exactly 1 DB lock row.
/// </summary>
[Trait("Tier", "Tier4")]
[Trait("Scenario", "Scenario2_HighConcurrencyContention")]
public class Scenario2_HighConcurrencyContentionTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public Scenario2_HighConcurrencyContentionTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier4")]
    public async Task Scenario2_TwentyConcurrentRequests_SameRoomAndNight_ExactlyOneWinner()
    {
        const int concurrentGuests = 20;
        const int targetRoomId = 101;

        // Dynamic collision-free date window for 1 night
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 7000 + Random.Shared.Next(10, 1000), stayDuration: 1);

        // Pre-register and acquire tokens for all 20 competing threads
        var tokens = new string[concurrentGuests];
        for (var i = 0; i < concurrentGuests; i++)
        {
            var username = $"contender_{i}_{Guid.NewGuid():N}"[..18];
            tokens[i] = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, username, "ContenderPass123!", "Guest");
        }

        // Thread synchronization barrier to ensure simultaneous dispatch
        using var barrier = new ManualResetEventSlim(false);
        var responses = new ConcurrentBag<(int Index, HttpStatusCode StatusCode, string Body)>();

        var tasks = Enumerable.Range(0, concurrentGuests).Select(async index =>
        {
            await Task.Yield();
            barrier.Wait(); // All 20 threads block until released simultaneously

            var payload = new
            {
                roomId = targetRoomId,
                guestName = $"Contender #{index}",
                guestEmail = $"contender_{index}@example.com",
                checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };

            var resp = await _gatewayClient.CreateBookingAsync(payload, tokens[index]);
            var body = await resp.Content.ReadAsStringAsync();
            responses.Add((index, resp.StatusCode, body));
        }).ToArray();

        // Release all 20 threads simultaneously
        barrier.Set();
        await Task.WhenAll(tasks);

        // Assertions: Exactly 1 winner (201 Created)
        var successfulBookings = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        var rejectedBookings = responses.Where(r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest).ToList();

        successfulBookings.Should().ContainSingle(
            "Under high concurrency contention for the same room and stay night, exactly 1 booking must succeed"
        );

        rejectedBookings.Count.Should().Be(
            concurrentGuests - 1,
            $"All {concurrentGuests - 1} losing requests must be rejected with 409 Conflict, 422, or 400"
        );

        // Extract winner's booking ID
        var winnerDoc = JsonDocument.Parse(successfulBookings.Single().Body);
        var winnerBookingId = winnerDoc.RootElement.GetProperty("id").GetInt32();
        winnerBookingId.Should().BeGreaterThan(0);

        // Database Lock Invariant Check
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*), booking_id 
            FROM room_night_locks 
            WHERE room_id = @roomId AND stay_date = @stayDate
            GROUP BY booking_id";
        cmd.Parameters.AddWithValue("@roomId", targetRoomId);
        cmd.Parameters.AddWithValue("@stayDate", checkIn.ToString("yyyy-MM-dd"));

        await using var reader = await cmd.ExecuteReaderAsync();
        var lockRows = 0;
        while (await reader.ReadAsync())
        {
            lockRows++;
            var lockCount = reader.GetInt64(0);
            var lockedBookingId = reader.GetInt32(1);

            lockCount.Should().Be(1, "Exactly one lock record must exist for the room and stay date");
            lockedBookingId.Should().Be(winnerBookingId, "The database lock must belong exclusively to the winner");
        }

        lockRows.Should().Be(1, "There must be exactly 1 lock group in room_night_locks table");
    }
}
