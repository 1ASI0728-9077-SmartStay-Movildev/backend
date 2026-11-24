namespace SmartStay.Bookings.Domain.Model.Entities;

/// <summary>
/// Cotización temporal (2 minutos) emitida por Bookings.
/// No adquiere cerrojos físicos sobre noches pero garantiza precio y viabilidad temporal.
/// </summary>
public class BookingQuote
{
    public BookingQuote()
    {
        QuoteId = string.Empty;
        PurchaseIntentId = string.Empty;
        Currency = "USD";
        CreatedAtUtc = DateTime.UtcNow;
    }

    public BookingQuote(
        string quoteId,
        int roomId,
        int hotelId,
        int guestUserId,
        string purchaseIntentId,
        DateTime checkInDate,
        DateTime checkOutDate,
        int guests,
        decimal nightlyPrice,
        decimal stayTotal,
        string currency = "USD",
        int ttlMinutes = 2) : this()
    {
        QuoteId = quoteId;
        RoomId = roomId;
        HotelId = hotelId;
        GuestUserId = guestUserId;
        PurchaseIntentId = purchaseIntentId;
        CheckInDate = checkInDate;
        CheckOutDate = checkOutDate;
        Guests = guests;
        NightlyPrice = nightlyPrice;
        StayTotal = stayTotal;
        Currency = currency;
        IsUsed = false;
        CreatedAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(ttlMinutes);
    }

    public string QuoteId { get; private set; }
    public int RoomId { get; private set; }
    public int HotelId { get; private set; }
    public int GuestUserId { get; private set; }
    public string PurchaseIntentId { get; private set; }
    public DateTime CheckInDate { get; private set; }
    public DateTime CheckOutDate { get; private set; }
    public int Guests { get; private set; }
    public decimal NightlyPrice { get; private set; }
    public decimal StayTotal { get; private set; }
    public string Currency { get; private set; }
    public bool IsUsed { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public bool IsExpired => DateTime.UtcNow > ExpiresAtUtc;

    public void MarkAsUsed()
    {
        IsUsed = true;
    }
}
