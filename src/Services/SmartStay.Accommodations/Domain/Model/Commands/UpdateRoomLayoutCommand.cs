namespace BackendAwSmartstay.API.Accommodations.Domain.Model.Commands;

/// <summary>
/// Command that updates the saved room layout in JSON format.
/// </summary>
/// <param name="RoomId">The room identifier.</param>
/// <param name="LayoutJson">The serialized room layout.</param>
public record UpdateRoomLayoutCommand(int RoomId, string LayoutJson);
