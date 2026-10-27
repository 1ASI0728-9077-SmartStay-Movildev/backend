namespace SmartStay.Analytics.Domain.Model.Entities;

public class BookingFact
{
    public BookingFact() { }

    public BookingFact(int bookingId, int roomId, int hotelId, string status, DateTime checkInDate, DateTime checkOutDate, DateTime occurredAtUtc)
    {
        BookingId = bookingId;
        RoomId = roomId;
        HotelId = hotelId;
        Status = status;
        CheckInDate = checkInDate;
        CheckOutDate = checkOutDate;
        NightsCount = Math.Max(1, (checkOutDate.Date - checkInDate.Date).Days);
        OccurredAtUtc = occurredAtUtc;
    }

    public int BookingId { get; private set; }
    public int RoomId { get; private set; }
    public int HotelId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime CheckInDate { get; private set; }
    public DateTime CheckOutDate { get; private set; }
    public int NightsCount { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public void UpdateStatus(string status)
    {
        Status = status;
    }
}
