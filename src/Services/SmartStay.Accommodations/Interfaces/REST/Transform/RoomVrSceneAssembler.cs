using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using SmartStay.Contracts.Http;

namespace BackendAwSmartstay.API.Accommodations.Interfaces.REST.Transform;

public static class RoomVrSceneAssembler
{
    private const double DoorHeightM = 2.10;
    private const double DoorSillM = 0.0;
    private const double WindowSillM = 0.90;
    private const double WindowHeightM = 1.20;
    private const double CeilingMarginM = 0.05;

    public static RoomVrSceneResponse ToVrScene(Room room)
    {
        var layout = RoomLayoutValidator.Deserialize(room.Layout);
        if (layout is null)
        {
            throw new InvalidOperationException($"Room {room.Id} has no saved layout.");
        }

        var preview = RoomLayoutValidator.Validate(layout);
        var width = layout.WidthM;
        var length = layout.LengthM;
        var height = layout.HeightM;
        var area = Math.Round(width * length, 2);

        var walls = BuildWalls(width, length, height);
        var beds = BuildBeds(layout.Beds, preview.BedSummary);
        var spawn = ComputeSpawn(layout, width, length, height);
        var panels = BuildPanels(area, layout.Floor, preview.BedSummary, preview.GuestCapacity, width, length);

        return new RoomVrSceneResponse(
            RoomId: room.Id,
            Units: "meters",
            Xr: new RoomVrXr(
                Mode: "immersive-vr",
                ReferenceSpace: "local-floor",
                RequiredFeatures: ["local-floor"],
                OptionalFeatures: ["bounded-floor"]),
            Room: new RoomVrRoom(
                WidthM: width,
                LengthM: length,
                HeightM: height,
                AreaM2: area,
                Floor: layout.Floor,
                WallColor: layout.WallColor),
            Walls: walls,
            Beds: beds,
            Spawn: spawn,
            Panels: panels);
    }

    private static IReadOnlyList<RoomVrWall> BuildWalls(double width, double length, double height)
    {
        var northStart = new RoomVrPoint(0, 0);
        var northEnd = new RoomVrPoint(width, 0);
        var southStart = new RoomVrPoint(0, length);
        var southEnd = new RoomVrPoint(width, length);
        var eastStart = new RoomVrPoint(width, 0);
        var eastEnd = new RoomVrPoint(width, length);
        var westStart = new RoomVrPoint(0, 0);
        var westEnd = new RoomVrPoint(0, length);

        return
        [
            new("N", northStart, northEnd, height, BuildOpeningList("N", height, new[] { new RoomVrOpening("window", 1.0, 2.0, WindowSillM, WindowHeightM) })),
            new("S", southStart, southEnd, height, BuildOpeningList("S", height, new[] { new RoomVrOpening("door", 0.4, 0.9, DoorSillM, DoorHeightM) })),
            new("E", eastStart, eastEnd, height, []),
            new("W", westStart, westEnd, height, [])
        ];
    }

    private static IReadOnlyList<RoomVrOpening> BuildOpeningList(string wall, double height, IEnumerable<RoomVrOpening> openings)
    {
        var list = new List<RoomVrOpening>();
        foreach (var opening in openings)
        {
            var adjustedHeight = Math.Min(opening.HeightM, Math.Max(0.0, height - CeilingMarginM));
            list.Add(new RoomVrOpening(opening.Kind, opening.OffsetM, opening.WidthM, opening.SillM, adjustedHeight));
        }

        return list;
    }

    private static IReadOnlyList<RoomVrBed> BuildBeds(IReadOnlyList<RoomBedPlacement>? placements, string bedSummary)
    {
        if (placements is null || placements.Count == 0)
        {
            return [];
        }

        var catalog = RoomLayoutValidator.Catalog;
        var specMap = catalog.Beds.ToDictionary(b => b.Type, b => b, StringComparer.OrdinalIgnoreCase);
        var result = new List<RoomVrBed>();

        foreach (var placement in placements)
        {
            var matched = specMap.TryGetValue(placement.Type, out var bedSpec) ? bedSpec : catalog.Beds[0];
            var width = bedSpec?.WidthM ?? matched.WidthM;
            var length = bedSpec?.LengthM ?? matched.LengthM;
            var effectiveWidth = placement.RotationDeg % 180 == 0 ? width : length;
            var effectiveLength = placement.RotationDeg % 180 == 0 ? length : width;

            var rotation = placement.RotationDeg;
            result.Add(new RoomVrBed(
                Type: placement.Type,
                Label: matched.Label,
                X: placement.X,
                Z: placement.Z,
                RotationDeg: rotation,
                WidthM: effectiveWidth,
                LengthM: effectiveLength,
                HeightM: 0.5));
        }

        return result;
    }

    private static RoomVrSpawn ComputeSpawn(RoomLayoutDto layout, double roomWidth, double roomLength, double roomHeight)
    {
        var doorWall = layout.Door.Wall;
        var doorOffset = layout.Door.Offset;
        var doorWidth = layout.Door.Width;
        var margin = 0.3;

        double x;
        double z;
        double yaw;
        double dx;
        double dz;

        switch (doorWall)
        {
            case "N":
                x = Clamp(doorOffset + (doorWidth / 2.0), margin, roomWidth - margin);
                z = Clamp(roomLength - 1.0, margin, roomLength - margin);
                dx = 0.0;
                dz = -1.0;
                yaw = Math.Atan2(-dx, -dz) * 180.0 / Math.PI;
                break;
            case "S":
                x = Clamp(doorOffset + (doorWidth / 2.0), margin, roomWidth - margin);
                z = Clamp(1.0, margin, roomLength - margin);
                dx = 0.0;
                dz = 1.0;
                yaw = Math.Atan2(-dx, -dz) * 180.0 / Math.PI;
                break;
            case "E":
                x = Clamp(roomWidth - 1.0, margin, roomWidth - margin);
                z = Clamp(doorOffset + (doorWidth / 2.0), margin, roomLength - margin);
                dx = -1.0;
                dz = 0.0;
                yaw = Math.Atan2(-dx, -dz) * 180.0 / Math.PI;
                break;
            case "W":
                x = Clamp(1.0, margin, roomWidth - margin);
                z = Clamp(doorOffset + (doorWidth / 2.0), margin, roomLength - margin);
                dx = 1.0;
                dz = 0.0;
                yaw = Math.Atan2(-dx, -dz) * 180.0 / Math.PI;
                break;
            default:
                x = Clamp(roomWidth / 2.0, margin, roomWidth - margin);
                z = Clamp(roomLength / 2.0, margin, roomLength - margin);
                dx = 0.0;
                dz = -1.0;
                yaw = 0.0;
                break;
        }

        return new RoomVrSpawn(x, 1.6, z, yaw);
    }

    private static IReadOnlyList<RoomVrPanel> BuildPanels(
        double area,
        string floor,
        string bedSummary,
        int guestCapacity,
        double width,
        double length)
    {
        var wallX = width / 2.0;
        var wallZ = length / 2.0;

        return
        [
            new("area", "Área", $"{area:F0} m²", wallX - 0.25, 1.5, 0.4, 0),
            new("floor", "Piso", floor, wallX - 0.25, 1.5, 1.2, 0),
            new("beds", "Camas", bedSummary, wallX - 0.25, 1.5, 2.0, 0),
            new("capacity", "Capacidad", $"{guestCapacity} huésped(es)", wallX - 0.25, 1.5, 2.8, 0)
        ];
    }

    private static double Clamp(double value, double min, double max)
        => Math.Min(Math.Max(value, min), max);
}
