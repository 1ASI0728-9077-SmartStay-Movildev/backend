using SmartStay.Bookings.Domain.Model.Aggregates;

namespace SmartStay.Bookings.Domain.Model.Entities;

/// <summary>
/// Bloqueo exclusivo de habitación por noche materializado en SQL.
/// La clave primaria compuesta (RoomId, StayDate) previene físicamente reservas dobles concurrentes.
/// </summary>
public class RoomNightLock
{
    public RoomNightLock() { }

    public RoomNightLock(int roomId, DateOnly stayDate, int bookingId)
    {
        RoomId = roomId;
        StayDate = stayDate;
        BookingId = bookingId;
    }

    public int RoomId { get; private set; }
    public DateOnly StayDate { get; private set; }
    public int BookingId { get; private set; }

    public Booking? Booking { get; private set; }
}
