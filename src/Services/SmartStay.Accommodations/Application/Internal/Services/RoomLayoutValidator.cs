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

        if (layout.WidthM <= 0 || layout.WidthM < 2 || layout.WidthM > 12)
        {
            errors.Add("El ancho debe estar entre 2 m y 12 m.");
        }

        if (layout.LengthM <= 0 || layout.LengthM < 2 || layout.LengthM > 12)
        {
            errors.Add("El largo debe estar entre 2 m y 12 m.");
        }

        if (layout.HeightM <= 0 || layout.HeightM < 2.2 || layout.HeightM > 4.0)
        {
            errors.Add("La altura debe estar entre 2.2 m y 4.0 m.");
        }

        if (string.IsNullOrWhiteSpace(layout.Floor))
        {
            errors.Add("Debe indicar el tipo de piso.");
        }

        if (string.IsNullOrWhiteSpace(layout.WallColor))
        {
            errors.Add("Debe indicar el color de pared.");
        }

        var area = Math.Round(layout.WidthM * layout.LengthM, 2);
        var totalGuests = 0;
        var bedSummary = "Sin camas";

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

            foreach (var bed in layout.Beds)
            {
                if (!specMap.ContainsKey(bed.Type))
                {
                    errors.Add($"Tipo de cama no soportado: {bed.Type}.");
                    continue;
                }

                totalGuests += specMap[bed.Type].Guests;
                bedNames.Add(specMap[bed.Type].Label);
            }

            if (bedNames.Count > 0)
            {
                bedSummary = string.Join(", ", bedNames.Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }

        if (layout.Windows is not null && layout.Windows.Count > Catalog.MaxWindows)
        {
            errors.Add($"El máximo de ventanas es {Catalog.MaxWindows}.");
        }

        if (layout.Door is null)
        {
            errors.Add("Debe definir la puerta.");
        }
        else
        {
            if (layout.Door.Width <= 0 || layout.Door.Width > 2.5)
            {
                errors.Add("La puerta debe tener un ancho válido.");
            }

            if (!"NSEW".Contains(layout.Door.Wall, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("La puerta debe estar orientada a una pared válida (N/S/E/W). ");
            }
        }

        if (layout.Windows is not null)
        {
            foreach (var window in layout.Windows)
            {
                if (!"NSEW".Contains(window.Wall, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("Las ventanas requieren pared válida (N/S/E/W).");
                }

                if (window.Width <= 0 || window.Width > 3.5)
                {
                    errors.Add("El ancho de la ventana debe estar entre 0 y 3.5 m.");
                }
            }
        }

        var isValid = errors.Count == 0;
        return new(isValid, Math.Round(area, 2), totalGuests, bedSummary, errors, layout);
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
}
