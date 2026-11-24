using System;
using System.Threading;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Configuration;

namespace SmartStay.E2E.Tests.Infrastructure.Helpers;

public static class TestDateHelper
{
    private static readonly DateTime BaseCheckInDate;
    private static int _offsetCounter = 0;

    static TestDateHelper()
    {
        try
        {
            using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(stay_date), '2030-01-01') FROM room_night_locks;";
            var result = cmd.ExecuteScalar();
            if (result != null && DateTime.TryParse(result.ToString(), out var maxStayDate))
            {
                BaseCheckInDate = maxStayDate.Date.AddDays(10);
                return;
            }
        }
        catch
        {
            // Fallback if DB query fails
        }

        BaseCheckInDate = DateTime.UtcNow.Date.AddYears(150);
    }

    public static (DateTime CheckIn, DateTime CheckOut) GetStayDates(int testOffset = 0, int stayDuration = 2)
    {
        var step = Interlocked.Add(ref _offsetCounter, 10);
        var checkIn = BaseCheckInDate.AddDays(step + testOffset);
        var checkOut = checkIn.AddDays(stayDuration);
        return (checkIn, checkOut);
    }
}
