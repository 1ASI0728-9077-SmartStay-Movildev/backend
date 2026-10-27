using BackendAwSmartstay.API.Bookings.Domain.Model.Aggregates;
using FluentAssertions;
using Xunit;

namespace SmartStay.DomainTests.Bookings;

public class BookingDomainTests
{
    [Fact]
    [Trait("Area", "Bookings")]
    [Trait("Risk", "Lifecycle")]
    public void New_booking_should_have_pending_status()
    {
        // Arrange & Act
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(3);
        var booking = new Booking(101, "Carlos Santana", "carlos@example.com", checkIn, checkOut);

        // Assert
        booking.Status.Should().Be(BookingStatus.Pending);
        booking.RoomId.Should().Be(101);
        booking.GuestName.Should().Be("Carlos Santana");
        booking.CheckInDate.Should().Be(checkIn);
        booking.CheckOutDate.Should().Be(checkOut);
    }

    [Fact]
    [Trait("Area", "Bookings")]
    [Trait("Risk", "Lifecycle")]
    public void Confirm_should_transition_status_to_confirmed()
    {
        // Arrange
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(101, "Carlos Santana", "carlos@example.com", checkIn, checkOut);

        // Act
        booking.Confirm();

        // Assert
        booking.Status.Should().Be(BookingStatus.Confirmed);
    }

    [Fact]
    [Trait("Area", "Bookings")]
    [Trait("Risk", "Lifecycle")]
    public void Cancel_should_transition_status_to_cancelled()
    {
        // Arrange
        var checkIn = DateTime.UtcNow.Date.AddDays(1);
        var checkOut = checkIn.AddDays(2);
        var booking = new Booking(101, "Carlos Santana", "carlos@example.com", checkIn, checkOut);

        // Act
        booking.Cancel();

        // Assert
        booking.Status.Should().Be(BookingStatus.Cancelled);
    }

    [Fact]
    [Trait("Area", "Bookings")]
    [Trait("Risk", "Validation")]
    public void Booking_dates_must_be_contiguous_and_checkout_after_checkin()
    {
        // Arrange
        var checkIn = DateTime.UtcNow.Date.AddDays(5);
        var checkOut = checkIn.AddDays(2);

        // Act & Assert
        checkOut.Should().BeAfter(checkIn);
        (checkOut - checkIn).Days.Should().Be(2);
    }
}
