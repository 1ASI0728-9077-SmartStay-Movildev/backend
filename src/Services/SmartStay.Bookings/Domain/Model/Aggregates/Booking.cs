using SmartStay.Bookings.Domain.Model.Entities;

namespace SmartStay.Bookings.Domain.Model.Aggregates;

public enum BookingStatus
{
    PendingPayment = 0,
    Confirmed = 1,
    Cancelled = 2,
    Expired = 3
}

/// <summary>
/// Agregado raíz de Reservas. Autoridad exclusiva sobre disponibilidad temporal de habitaciones.
/// </summary>
public class Booking
{
    private readonly List<RoomNightLock> _nightLocks = [];

    public Booking()
    {
        GuestName = string.Empty;
        GuestEmail = string.Empty;
        Currency = "USD";
        Status = BookingStatus.PendingPayment;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Booking(
        int roomId,
        int hotelId,
        string guestName,
        string guestEmail,
        DateTime checkInDate,
        DateTime checkOutDate,
        decimal nightlyPrice,
        decimal totalAmount,
        string currency = "USD",
        int ttlMinutes = 15,
        int guestUserId = 0) : this()
    {
        RoomId = roomId;
        HotelId = hotelId;
        GuestName = guestName;
        GuestEmail = guestEmail;
        GuestUserId = guestUserId;
        CheckInDate = checkInDate;
        CheckOutDate = checkOutDate;
        NightlyPrice = nightlyPrice;
        TotalAmount = totalAmount;
        Currency = currency;
        Status = BookingStatus.PendingPayment;
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(ttlMinutes);
    }

    public int Id { get; private set; }
    public int RoomId { get; private set; }
    public int HotelId { get; private set; }
    public int GuestUserId { get; private set; }
    public string GuestName { get; private set; }
    public string GuestEmail { get; private set; }
    public DateTime CheckInDate { get; private set; }
    public DateTime CheckOutDate { get; private set; }
    public decimal NightlyPrice { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; }
    public BookingStatus Status { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<RoomNightLock> NightLocks => _nightLocks.AsReadOnly();

    public void AddNightLock(RoomNightLock lockEntry)
    {
        if (!_nightLocks.Any(l => l.RoomId == lockEntry.RoomId && l.StayDate == lockEntry.StayDate))
        {
            _nightLocks.Add(lockEntry);
        }
    }

    public void ClearNightLocks()
    {
        _nightLocks.Clear();
    }

    public void Confirm()
    {
        if (Status != BookingStatus.PendingPayment)
            throw new InvalidOperationException($"Cannot confirm booking from status '{Status}'.");

        Status = BookingStatus.Confirmed;
        ExpiresAtUtc = null;
    }

    public void Cancel()
    {
        if (Status == BookingStatus.Cancelled)
            return;

        Status = BookingStatus.Cancelled;
        ExpiresAtUtc = null;
        ClearNightLocks();
    }

    public void Expire()
    {
        if (Status != BookingStatus.PendingPayment)
            return;

        Status = BookingStatus.Expired;
        ClearNightLocks();
    }
}
