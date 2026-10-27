using FluentAssertions;
using SmartStay.Bookings.Domain.Model.Aggregates;
using Xunit;

namespace SmartStay.Bookings.Tests;

public class BookingLifecycleTests
{
    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Lifecycle")]
    public void New_booking_should_have_pending_payment_status()
    {
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(3);
        var booking = new Booking(101, 1, "Carlos Santana", "carlos@example.com", checkIn, checkOut, 150.00m, 300.00m, "USD");

        booking.Status.Should().Be(BookingStatus.PendingPayment);
        booking.RoomId.Should().Be(101);
        booking.GuestName.Should().Be("Carlos Santana");
        booking.CheckInDate.Should().Be(checkIn);
        booking.CheckOutDate.Should().Be(checkOut);
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Lifecycle")]
    public void Confirm_should_transition_status_to_confirmed()
    {
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(101, 1, "Carlos Santana", "carlos@example.com", checkIn, checkOut, 150.00m, 150.00m, "USD");

        booking.Confirm();

        booking.Status.Should().Be(BookingStatus.Confirmed);
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Lifecycle")]
    public void Cancel_should_transition_status_to_cancelled_and_clear_locks()
    {
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(101, 1, "Carlos Santana", "carlos@example.com", checkIn, checkOut, 150.00m, 150.00m, "USD");

        booking.Cancel();

        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.NightLocks.Should().BeEmpty();
    }
}
