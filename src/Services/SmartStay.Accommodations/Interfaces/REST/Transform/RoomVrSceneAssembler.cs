using System.Globalization;
using BackendAwSmartstay.API.Accommodations.Application.Internal.Services;
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
            throw new InvalidOperationException($"La habitación {room.Id} no tiene un layout guardado.");
        }

        var preview = RoomLayoutValidator.Validate(layout);
        if (!preview.IsValid)
        {
            throw new InvalidOperationException("El layout guardado no es válido para construir la escena VR.");
        }

        var width = layout.WidthM;
        var length = layout.LengthM;
        var height = layout.HeightM;
        var area = Math.Round(width * length, 2, MidpointRounding.AwayFromZero);

        var walls = BuildWalls(width, length, height, layout);
        var beds = BuildBeds(layout.Beds, width, length);
        var spawn = ComputeSpawn(layout, width, length);
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

    private static IReadOnlyList<RoomVrWall> BuildWalls(double width, double length, double height, RoomLayoutDto layout)
    {
        var northStart = new RoomVrPoint(-width / 2d, -length / 2d);
        var northEnd = new RoomVrPoint(width / 2d, -length / 2d);
        var southStart = new RoomVrPoint(-width / 2d, length / 2d);
        var southEnd = new RoomVrPoint(width / 2d, length / 2d);
        var eastStart = new RoomVrPoint(width / 2d, -length / 2d);
        var eastEnd = new RoomVrPoint(width / 2d, length / 2d);
        var westStart = new RoomVrPoint(-width / 2d, -length / 2d);
        var westEnd = new RoomVrPoint(-width / 2d, length / 2d);

        var openings = new Dictionary<string, IReadOnlyList<RoomVrOpening>>
        {
            ["N"] = BuildOpeningList(layout, "N", height),
            ["S"] = BuildOpeningList(layout, "S", height),
            ["E"] = BuildOpeningList(layout, "E", height),
            ["W"] = BuildOpeningList(layout, "W", height)
        };

        return
        [
            new("N", northStart, northEnd, height, openings["N"]),
            new("S", southStart, southEnd, height, openings["S"]),
            new("E", eastStart, eastEnd, height, openings["E"]),
            new("W", westStart, westEnd, height, openings["W"])
        ];
    }

    private static IReadOnlyList<RoomVrOpening> BuildOpeningList(RoomLayoutDto layout, string wall, double height)
    {
        var list = new List<RoomVrOpening>();
        var doorOpening = layout.Door;
        if (doorOpening is not null && string.Equals(doorOpening.Wall, wall, StringComparison.OrdinalIgnoreCase))
        {
            list.Add(new RoomVrOpening("door", doorOpening.Offset, doorOpening.Width, DoorSillM, Math.Min(DoorHeightM, Math.Max(0.0, height - CeilingMarginM))));
        }

        foreach (var window in layout.Windows ?? [])
        {
            if (string.Equals(window.Wall, wall, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new RoomVrOpening("window", window.Offset, window.Width, WindowSillM, Math.Min(WindowHeightM, Math.Max(0.0, height - CeilingMarginM))));
            }
        }

        return list;
    }

    private static IReadOnlyList<RoomVrBed> BuildBeds(IReadOnlyList<RoomBedPlacement>? placements, double roomWidth, double roomLength)
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
            var width = Math.Abs(placement.RotationDeg % 180d) < 0.0001 ? matched.WidthM : matched.LengthM;
            var length = Math.Abs(placement.RotationDeg % 180d) < 0.0001 ? matched.LengthM : matched.WidthM;
            var rotation = (360 - placement.RotationDeg) % 360;
            var x = placement.X - roomWidth / 2d;
            var z = placement.Z - roomLength / 2d;
            result.Add(new RoomVrBed(
                Type: placement.Type,
                Label: matched.Label,
                X: x,
                Z: z,
                RotationDeg: rotation,
                WidthM: width,
                LengthM: length,
                HeightM: 0.5));
        }

        return result;
    }

    private static RoomVrSpawn ComputeSpawn(RoomLayoutDto layout, double roomWidth, double roomLength)
    {
        var doorWall = layout.Door?.Wall ?? "N";
        var doorOffset = layout.Door?.Offset ?? 0d;
        var doorWidth = layout.Door?.Width ?? 0d;

        double x;
        double z;

        const double marginFromWall = 0.3d;
        const double eyeHeight = 1.6d;
        const double doorLookOffset = 1.0d;

        switch (doorWall)
        {
            case "N":
                x = doorOffset + (doorWidth / 2d) - roomWidth / 2d;
                z = -roomLength / 2d + doorLookOffset;
                break;
            case "S":
                x = doorOffset + (doorWidth / 2d) - roomWidth / 2d;
                z = roomLength / 2d - doorLookOffset;
                break;
            case "E":
                x = roomWidth / 2d - doorLookOffset;
                z = doorOffset + (doorWidth / 2d) - roomLength / 2d;
                break;
            case "W":
                x = -roomWidth / 2d + doorLookOffset;
                z = doorOffset + (doorWidth / 2d) - roomLength / 2d;
                break;
            default:
                x = 0d;
                z = 0d;
                break;
        }

        var clampedX = Math.Clamp(x, -roomWidth / 2d + marginFromWall, roomWidth / 2d - marginFromWall);
        var clampedZ = Math.Clamp(z, -roomLength / 2d + marginFromWall, roomLength / 2d - marginFromWall);

        var centerX = 0d;
        var centerZ = 0d;
        var dx = centerX - clampedX;
        var dz = centerZ - clampedZ;
        var yawDeg = Math.Atan2(-dx, -dz) * 180d / Math.PI;

        return new RoomVrSpawn(
            X: clampedX,
            Y: eyeHeight,
            Z: clampedZ,
            YawDeg: yawDeg);
    }

    private static IReadOnlyList<RoomVrPanel> BuildPanels(
        double area,
        string floor,
        string bedSummary,
        int guestCapacity,
        double width,
        double length)
    {
        var wallX = width / 2d;

        return
        [
            new("area", "Área", string.Create(CultureInfo.InvariantCulture, $"{area:F0} m²"), wallX - 0.25, 1.5, 0.4, 0),
            new("floor", "Piso", floor, wallX - 0.25, 1.5, 1.2, 0),
            new("beds", "Camas", bedSummary, wallX - 0.25, 1.5, 2.0, 0),
            new("capacity", "Capacidad", string.Create(CultureInfo.InvariantCulture, $"{guestCapacity} huésped(es)"), wallX - 0.25, 1.5, 2.8, 0)
        ];
    }
}
