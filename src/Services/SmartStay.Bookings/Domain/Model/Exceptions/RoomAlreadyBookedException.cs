namespace SmartStay.Bookings.Domain.Model.Exceptions;

public class RoomAlreadyBookedException : Exception
{
    public int RoomId { get; }
    public DateOnly CollisionDate { get; }

    public RoomAlreadyBookedException(int roomId, DateOnly collisionDate)
        : base($"Room {roomId} is already booked for date {collisionDate:yyyy-MM-dd}.")
    {
        RoomId = roomId;
        CollisionDate = collisionDate;
    }
}
