using BackendAwSmartstay.API.Accommodations.Application.Internal.Services;
using FluentAssertions;
using SmartStay.Contracts.Http;

namespace SmartStay.Accommodations.Tests;

public class RoomLayoutValidatorTests
{
    private static RoomLayoutDto MakeValidLayout() => new(
        WidthM: 4,
        LengthM: 5,
        HeightM: 2.8,
        Floor: "Wood",
        WallColor: "#F3E9D7",
        Beds:
        [
            new RoomBedPlacement("OnePlaza", 1.5, 1.5, 0),
            new RoomBedPlacement("OnePlaza", 2.8, 3.3, 0)
        ],
        Windows:
        [
            new RoomOpening("N", 1.0, 1.2),
            new RoomOpening("E", 0.8, 1.5)
        ],
        Door: new RoomOpening("S", 0.5, 0.9));

    [Fact]
    public void Validate_should_accept_valid_layout()
    {
        var result = RoomLayoutValidator.Validate(MakeValidLayout());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1.9)]
    [InlineData(12.1)]
    public void Validate_should_reject_invalid_room_dimensions(double size)
    {
        var layout = MakeValidLayout() with { WidthM = size };

        var result = RoomLayoutValidator.Validate(layout);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ancho", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_should_reject_bed_outside_room()
    {
        var layout = MakeValidLayout() with
        {
            Beds = [new RoomBedPlacement("OnePlaza", 3.9, 1.5, 0)]
        };

        var result = RoomLayoutValidator.Validate(layout);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("fuera del cuarto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_should_reject_overlapping_beds()
    {
        var layout = MakeValidLayout() with
        {
            Beds =
            [
                new RoomBedPlacement("OnePlaza", 2.0, 2.0, 0),
                new RoomBedPlacement("OnePlaza", 2.2, 2.0, 0)
            ]
        };

        var result = RoomLayoutValidator.Validate(layout);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Las camas no pueden solaparse entre sí.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("NS")]
    [InlineData("X")]
    public void Validate_should_reject_invalid_wall_names(string wall)
    {
        var layout = MakeValidLayout() with { Door = new RoomOpening(wall, 0.5, 0.9) };

        var result = RoomLayoutValidator.Validate(layout);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("pared válida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_should_reject_window_outside_wall()
    {
        var layout = MakeValidLayout() with
        {
            Windows = [new RoomOpening("N", 3.5, 1.5)]
        };

        var result = RoomLayoutValidator.Validate(layout);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("excede la longitud", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_should_reject_door_overlapping_window_on_same_wall()
    {
        var layout = MakeValidLayout() with
        {
            Door = new RoomOpening("N", 1.0, 0.9),
            Windows = [new RoomOpening("N", 0.7, 0.8)]
        };

        var result = RoomLayoutValidator.Validate(layout);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("La puerta y la ventana no pueden solaparse en la misma pared.");
    }

    [Fact]
    public void Validate_should_reject_invalid_wall_color()
    {
        var layout = MakeValidLayout() with { WallColor = "#GGGGGG" };

        var result = RoomLayoutValidator.Validate(layout);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("hexadecimal", StringComparison.OrdinalIgnoreCase));
    }
}
