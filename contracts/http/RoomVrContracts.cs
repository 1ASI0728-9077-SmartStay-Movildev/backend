namespace SmartStay.Contracts.Http;

public sealed record RoomVrSceneResponse(
    int RoomId,
    string Units,
    RoomVrXr Xr,
    RoomVrRoom Room,
    IReadOnlyList<RoomVrWall> Walls,
    IReadOnlyList<RoomVrBed> Beds,
    RoomVrSpawn Spawn,
    IReadOnlyList<RoomVrPanel> Panels);

public sealed record RoomVrXr(
    string Mode,
    string ReferenceSpace,
    IReadOnlyList<string> RequiredFeatures,
    IReadOnlyList<string> OptionalFeatures);

public sealed record RoomVrRoom(
    double WidthM,
    double LengthM,
    double HeightM,
    double AreaM2,
    string Floor,
    string WallColor);

public sealed record RoomVrPoint(
    double X,
    double Z);

public sealed record RoomVrWall(
    string Id,
    RoomVrPoint Start,
    RoomVrPoint End,
    double HeightM,
    IReadOnlyList<RoomVrOpening> Openings);

public sealed record RoomVrOpening(
    string Kind,
    double OffsetM,
    double WidthM,
    double SillM,
    double HeightM);

public sealed record RoomVrBed(
    string Type,
    string Label,
    double X,
    double Z,
    double RotationDeg,
    double WidthM,
    double LengthM,
    double HeightM);

public sealed record RoomVrSpawn(
    double X,
    double Y,
    double Z,
    double YawDeg);

public sealed record RoomVrPanel(
    string Id,
    string Title,
    string Text,
    double X,
    double Y,
    double Z,
    double RotationY);
