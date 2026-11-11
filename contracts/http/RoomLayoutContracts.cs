namespace SmartStay.Contracts.Http;

public sealed record RoomBedCatalogItem(
    string Type,
    string Label,
    double WidthM,
    double LengthM,
    int Guests);

public sealed record RoomLayoutCatalog(
    IReadOnlyList<RoomBedCatalogItem> Beds,
    IReadOnlyList<string> Floors,
    double MinSideM,
    double MaxSideM,
    double MinHeightM,
    double MaxHeightM,
    int MaxBeds,
    int MaxWindows);

public sealed record RoomBedPlacement(
    string Type,
    double X,
    double Z,
    double RotationDeg);

public sealed record RoomOpening(
    string Wall,
    double Offset,
    double Width);

public sealed record RoomLayoutDto(
    double WidthM,
    double LengthM,
    double HeightM,
    string Floor,
    string WallColor,
    IReadOnlyList<RoomBedPlacement> Beds,
    IReadOnlyList<RoomOpening> Windows,
    RoomOpening Door);

public sealed record RoomLayoutView(
    int RoomId,
    double AreaM2,
    int GuestCapacity,
    string BedSummary,
    string? Floor,
    string? WallColor,
    RoomLayoutDto Layout);

public sealed record RoomLayoutPreviewResult(
    bool IsValid,
    double AreaM2,
    int GuestCapacity,
    string BedSummary,
    IReadOnlyList<string> Errors,
    RoomLayoutDto? Layout);
