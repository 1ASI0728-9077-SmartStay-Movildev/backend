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
        booking.ConcurrencyToken.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Lifecycle")]
    public void Confirm_should_transition_status_to_confirmed_and_rotate_token()
    {
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(101, 1, "Carlos Santana", "carlos@example.com", checkIn, checkOut, 150.00m, 150.00m, "USD");
        var initialToken = booking.ConcurrencyToken;

        booking.Confirm();

        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.ExpiresAtUtc.Should().BeNull();
        booking.ConcurrencyToken.Should().NotBe(initialToken);
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Security")]
    public void Confirm_When_Ttl_Expired_Should_Transition_To_Expired_And_Throw()
    {
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        // Expired TTL (-5 minutes)
        var booking = new Booking(101, 1, "Carlos Santana", "carlos@example.com", checkIn, checkOut, 150.00m, 150.00m, "USD", ttlMinutes: -5);

        var action = () => booking.Confirm();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*TTL has already expired*");
        booking.Status.Should().Be(BookingStatus.Expired);
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Lifecycle")]
    public void Cancel_should_transition_status_to_cancelled_and_clear_locks()
    {
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(101, 1, "Carlos Santana", "carlos@example.com", checkIn, checkOut, 150.00m, 150.00m, "USD");
        var initialToken = booking.ConcurrencyToken;

        booking.Cancel();

        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.NightLocks.Should().BeEmpty();
        booking.ConcurrencyToken.Should().NotBe(initialToken);
    }

    [Fact]
    [Trait("Service", "Bookings")]
    [Trait("Category", "Lifecycle")]
    public void Expire_should_transition_status_to_expired_and_clear_locks()
    {
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(101, 1, "Carlos Santana", "carlos@example.com", checkIn, checkOut, 150.00m, 150.00m, "USD", ttlMinutes: 15, guestUserId: 42);
        var initialToken = booking.ConcurrencyToken;

        booking.GuestUserId.Should().Be(42);
        booking.Expire();

        booking.Status.Should().Be(BookingStatus.Expired);
        booking.NightLocks.Should().BeEmpty();
        booking.ConcurrencyToken.Should().NotBe(initialToken);
    }
}
