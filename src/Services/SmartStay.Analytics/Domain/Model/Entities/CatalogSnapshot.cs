namespace SmartStay.Analytics.Domain.Model.Entities;

public class CatalogSnapshot
{
    public CatalogSnapshot() { }

    public CatalogSnapshot(int roomId, int hotelId, bool isBookable, decimal nightlyPrice)
    {
        RoomId = roomId;
        HotelId = hotelId;
        IsBookable = isBookable;
        NightlyPrice = nightlyPrice;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public int RoomId { get; private set; }
    public int HotelId { get; private set; }
    public bool IsBookable { get; private set; }
    public decimal NightlyPrice { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Update(bool isBookable, decimal nightlyPrice)
    {
        IsBookable = isBookable;
        NightlyPrice = nightlyPrice;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
