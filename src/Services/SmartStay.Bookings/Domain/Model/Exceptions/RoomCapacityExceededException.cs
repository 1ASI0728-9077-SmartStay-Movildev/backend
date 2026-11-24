namespace SmartStay.Bookings.Domain.Model.Exceptions;

public class RoomCapacityExceededException(int roomId, int requestedGuests, int capacity)
    : Exception($"Requested {requestedGuests} guests exceeds capacity of {capacity} for room {roomId}.")
{
    public int RoomId { get; } = roomId;
    public int RequestedGuests { get; } = requestedGuests;
    public int Capacity { get; } = capacity;
}

public class RoomNotBookableException(int roomId)
    : Exception($"Room {roomId} is not available for booking in the catalog.")
{
    public int RoomId { get; } = roomId;
}
