using System.Globalization;
using System.Text.Json;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using SmartStay.Contracts.Http;

namespace BackendAwSmartstay.API.Accommodations.Application.Internal.Services;

public static class RoomLayoutValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> ValidWalls = new(StringComparer.OrdinalIgnoreCase)
    {
        "N", "S", "E", "W"
    };

    private static readonly HashSet<string> ValidFloors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Wood", "Laminate", "Tile", "Carpet", "Concrete", "Porcelain"
    };

    public static RoomLayoutCatalog Catalog => new(
        Beds: [
            new("OnePlaza", "1 plaza", 0.9, 1.9, 1),
            new("OneAndHalfPlaza", "1½ plaza", 1.05, 1.9, 1),
            new("TwoPlazas", "2 plazas", 1.4, 1.9, 2),
            new("Queen", "Queen", 1.6, 2.0, 2),
            new("King", "King", 1.8, 2.0, 2)
        ],
        Floors: ["Wood", "Laminate", "Tile", "Carpet", "Concrete", "Porcelain"],
        MinSideM: 2.0,
        MaxSideM: 12.0,
        MinHeightM: 2.2,
        MaxHeightM: 4.0,
        MaxBeds: 4,
        MaxWindows: 6);

    public static RoomLayoutPreviewResult Validate(RoomLayoutDto? layout)
    {
        if (layout is null)
        {
            return new(false, 0, 0, "Sin layout", ["El layout es obligatorio."], null);
        }

        var errors = new List<string>();
        var width = layout.WidthM;
        var length = layout.LengthM;
        var height = layout.HeightM;
        var area = Math.Round(width * length, 2, MidpointRounding.AwayFromZero);
        var totalGuests = 0;
        var bedSummary = "Sin camas";

        if (width < Catalog.MinSideM || width > Catalog.MaxSideM)
        {
            errors.Add("El ancho debe estar entre 2 m y 12 m.");
        }

        if (length < Catalog.MinSideM || length > Catalog.MaxSideM)
        {
            errors.Add("El largo debe estar entre 2 m y 12 m.");
        }

        if (height < Catalog.MinHeightM || height > Catalog.MaxHeightM)
        {
            errors.Add("La altura debe estar entre 2.2 m y 4.0 m.");
        }

        if (string.IsNullOrWhiteSpace(layout.Floor) || !ValidFloors.Contains(layout.Floor))
        {
            errors.Add("El piso debe ser uno de: Wood, Laminate, Tile, Carpet, Concrete o Porcelain.");
        }

        if (!IsValidHexColor(layout.WallColor))
        {
            errors.Add("El color de pared debe ser un hexadecimal válido (#RGB o #RRGGBB).");
        }

        if (layout.Beds is null || layout.Beds.Count == 0)
        {
            errors.Add("Debe definir al menos una cama.");
        }
        else
        {
            if (layout.Beds.Count > Catalog.MaxBeds)
            {
                errors.Add($"El máximo de camas es {Catalog.MaxBeds}.");
            }

            var specMap = Catalog.Beds.ToDictionary(b => b.Type, b => b, StringComparer.OrdinalIgnoreCase);
            var bedNames = new List<string>();
            var bedPlacements = new List<(RoomBedPlacement Placement, RoomBedCatalogItem Spec)>();

            foreach (var bed in layout.Beds)
            {
                if (!specMap.TryGetValue(bed.Type, out var spec))
                {
                    errors.Add($"El tipo de cama '{bed.Type}' no está soportado.");
                    continue;
                }

                totalGuests += spec.Guests;
                bedNames.Add(spec.Label);

                if (Math.Abs(bed.RotationDeg % 90d) > 0.0001)
                {
                    errors.Add($"La cama '{bed.Type}' debe tener una rotación múltiplo de 90°.");
                }

                var effectiveWidth = Math.Abs(bed.RotationDeg % 180d) < 0.0001 ? spec.WidthM : spec.LengthM;
                var effectiveLength = Math.Abs(bed.RotationDeg % 180d) < 0.0001 ? spec.LengthM : spec.WidthM;
                var halfWidth = effectiveWidth / 2d;
                var halfLength = effectiveLength / 2d;

                if (bed.X - halfWidth < -0.0001 || bed.X + halfWidth > width + 0.0001 ||
                    bed.Z - halfLength < -0.0001 || bed.Z + halfLength > length + 0.0001)
                {
                    errors.Add($"La cama '{bed.Type}' queda fuera del cuarto.");
                }

                bedPlacements.Add((bed, spec));
            }

            for (var i = 0; i < bedPlacements.Count; i++)
            {
                for (var j = i + 1; j < bedPlacements.Count; j++)
                {
                    if (BedsOverlap(bedPlacements[i].Placement, bedPlacements[i].Spec, bedPlacements[j].Placement, bedPlacements[j].Spec))
                    {
                        errors.Add("Las camas no pueden solaparse entre sí.");
                    }
                }
            }

            if (bedNames.Count > 0)
            {
                bedSummary = string.Join(", ", bedNames.Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }

        if (layout.Windows is null)
        {
            layout = layout with { Windows = [] };
        }

        if (layout.Windows.Count > Catalog.MaxWindows)
        {
            errors.Add($"El máximo de ventanas es {Catalog.MaxWindows}.");
        }

        if (layout.Door is null)
        {
            errors.Add("Debe definir la puerta.");
        }
        else
        {
            ValidateDoor(layout.Door, width, length, errors);
        }

        if (layout.Windows is not null)
        {
            for (var i = 0; i < layout.Windows.Count; i++)
            {
                var window = layout.Windows[i];
                ValidateWindow(window, width, length, errors, i);
            }

            ValidateWindowCollisions(layout.Windows, layout.Door, width, length, errors);
        }

        var isValid = errors.Count == 0;
        return new(isValid, Math.Round(area, 2, MidpointRounding.AwayFromZero), totalGuests, bedSummary, errors, layout);
    }

    public static RoomLayoutView ToView(int roomId, Room room)
    {
        var layout = Deserialize(room.Layout);
        var preview = Validate(layout);
        return new(
            roomId,
            preview.AreaM2,
            preview.GuestCapacity,
            preview.BedSummary,
            layout?.Floor,
            layout?.WallColor,
            layout ?? new RoomLayoutDto(0, 0, 0, "Wood", "#EFE9DF", [], [], new("N", 0.3, 0.9)));
    }

    public static string Serialize(RoomLayoutDto layout)
        => JsonSerializer.Serialize(layout, JsonOptions);

    public static RoomLayoutDto? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RoomLayoutDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void ValidateDoor(RoomOpening door, double width, double length, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(door.Wall) || !ValidWalls.Contains(door.Wall))
        {
            errors.Add("La puerta debe estar en una pared válida (N, S, E o W).");
        }

        if (door.Width < 0.7 || door.Width > 1.2)
        {
            errors.Add("La puerta debe tener un ancho entre 0.7 m y 1.2 m.");
        }

        if (door.Offset < 0)
        {
            errors.Add("El offset de la puerta no puede ser negativo.");
        }

        var wallLength = GetWallLength(door.Wall, width, length);
        if (wallLength > 0 && door.Offset + door.Width > wallLength + 0.0001)
        {
            errors.Add("La puerta no puede salir del perímetro de la pared.");
        }
    }

    private static void ValidateWindow(RoomOpening window, double width, double length, List<string> errors, int index)
    {
        if (string.IsNullOrWhiteSpace(window.Wall) || !ValidWalls.Contains(window.Wall))
        {
            errors.Add("La ventana debe estar en una pared válida (N, S, E o W).");
            return;
        }

        if (window.Width < 0.4 || window.Width > 3.0)
        {
            errors.Add("La ventana debe tener un ancho entre 0.4 m y 3.0 m.");
        }

        if (window.Offset < 0)
        {
            errors.Add($"La ventana {index + 1} tiene un offset negativo.");
        }

        var wallLength = GetWallLength(window.Wall, width, length);
        if (wallLength > 0 && window.Offset + window.Width > wallLength + 0.0001)
        {
            errors.Add($"La ventana {index + 1} excede la longitud de su pared.");
        }
    }

    private static void ValidateWindowCollisions(IReadOnlyList<RoomOpening> windows, RoomOpening? door, double width, double length, List<string> errors)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            for (var j = i + 1; j < windows.Count; j++)
            {
                if (!string.Equals(windows[i].Wall, windows[j].Wall, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IntervalsOverlap(windows[i].Offset, windows[i].Offset + windows[i].Width, windows[j].Offset, windows[j].Offset + windows[j].Width))
                {
                    errors.Add("Las ventanas de una misma pared no pueden solaparse.");
                }
            }
        }

        if (door is not null && windows.Count > 0)
        {
            foreach (var window in windows)
            {
                if (!string.Equals(window.Wall, door.Wall, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IntervalsOverlap(window.Offset, window.Offset + window.Width, door.Offset, door.Offset + door.Width))
                {
                    errors.Add("La puerta y la ventana no pueden solaparse en la misma pared.");
                }
            }
        }
    }

    private static bool IntervalsOverlap(double startA, double endA, double startB, double endB)
        => startA < endB - 0.0001 && startB < endA - 0.0001;

    private static bool BedsOverlap(RoomBedPlacement a, RoomBedCatalogItem aSpec, RoomBedPlacement b, RoomBedCatalogItem bSpec)
    {
        var aWidth = Math.Abs(a.RotationDeg % 180d) < 0.0001 ? aSpec.WidthM : aSpec.LengthM;
        var aLength = Math.Abs(a.RotationDeg % 180d) < 0.0001 ? aSpec.LengthM : aSpec.WidthM;
        var bWidth = Math.Abs(b.RotationDeg % 180d) < 0.0001 ? bSpec.WidthM : bSpec.LengthM;
        var bLength = Math.Abs(b.RotationDeg % 180d) < 0.0001 ? bSpec.LengthM : bSpec.WidthM;

        return Math.Abs(a.X - b.X) <= (aWidth / 2d + bWidth / 2d) + 0.0001
            && Math.Abs(a.Z - b.Z) <= (aLength / 2d + bLength / 2d) + 0.0001;
    }

    private static double GetWallLength(string? wall, double width, double length)
    {
        if (string.IsNullOrWhiteSpace(wall))
        {
            return 0d;
        }

        return wall.Equals("N", StringComparison.OrdinalIgnoreCase) || wall.Equals("S", StringComparison.OrdinalIgnoreCase)
            ? width
            : wall.Equals("E", StringComparison.OrdinalIgnoreCase) || wall.Equals("W", StringComparison.OrdinalIgnoreCase)
                ? length
                : 0d;
    }

    private static bool IsValidHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!value.StartsWith('#'))
        {
            return false;
        }

        var color = value.Substring(1);
        return color.Length is 3 or 6 && color.All(c => char.IsLetterOrDigit(c) && "0123456789abcdefABCDEF".Contains(c));
    }
}

