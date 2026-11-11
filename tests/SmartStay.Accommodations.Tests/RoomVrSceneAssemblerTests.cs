using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Accommodations.Interfaces.REST.Transform;
using FluentAssertions;

namespace SmartStay.Accommodations.Tests;

public class RoomVrSceneAssemblerTests
{
    private static Room MakeRoom() => new()
    {
        // SetLayout is used in the aggregate; layout creation is kept compact for unit tests.
    };

    [Fact]
    public void ToVrScene_should_convert_room_coordinates_and_rotation()
    {
        var room = new Room();
        room.SetLayout("{\"widthM\":4,\"lengthM\":5,\"heightM\":2.8,\"floor\":\"Wood\",\"wallColor\":\"#F3E9D7\",\"beds\":[{\"type\":\"OnePlaza\",\"x\":2,\"z\":2.5,\"rotationDeg\":90}],\"windows\":[{\"wall\":\"N\",\"offset\":1,\"width\":1.2}],\"door\":{\"wall\":\"S\",\"offset\":1.5,\"width\":0.9}}"
        );

        var scene = RoomVrSceneAssembler.ToVrScene(room);

        scene.Beds.Should().ContainSingle();
        scene.Beds[0].X.Should().BeApproximately(0.0, 0.001);
        scene.Beds[0].Z.Should().BeApproximately(0.0, 0.001);
        scene.Beds[0].RotationDeg.Should().BeApproximately(270, 0.001);
    }

    [Theory]
    // Valores calculados a mano con origen en el centro del cuarto y la fórmula A-Frame
    // yaw = atan2(-dx, -dz), donde (dx, dz) = (centro - spawn).
    // Para un cuarto 4x5, doorOffset=1.5, doorWidth=0.9 y margen de 0.3 m:
    // N: spawn = (-0.05, -1.5) -> centro (0,0) => dx = +0.05, dz = +1.5 => yaw ≈ atan2(-0.05,-1.5) = -178.09°
    // S: spawn = (-0.05, +1.5) -> dx = +0.05, dz = -1.5 => yaw ≈ atan2(-0.05,+1.5) = -1.91°
    // E: spawn = (+1.0, -0.55) -> dx = -1.0, dz = +0.55 => yaw ≈ atan2(+1.0,-0.55) = +118.81°
    // W: spawn = (-1.0, -0.55) -> dx = +1.0, dz = +0.55 => yaw ≈ atan2(-1.0,-0.55) = -118.81°
    [InlineData("N", -0.05, -1.5, -178.09084756700364)]
    [InlineData("S", -0.05, 1.5, -1.9091524329963763)]
    [InlineData("E", 1.0, -0.55, 118.81079374297306)]
    [InlineData("W", -1.0, -0.55, -118.81079374297306)]
    public void ToVrScene_should_place_spawn_inside_room_and_face_center(string doorWall, double expectedX, double expectedZ, double expectedYaw)
    {
        var windowWall = doorWall switch
        {
            "N" => "E",
            "S" => "W",
            "E" => "N",
            "W" => "S",
            _ => "N"
        };

        var room = new Room();
        room.SetLayout($"{{\"widthM\":4,\"lengthM\":5,\"heightM\":2.8,\"floor\":\"Wood\",\"wallColor\":\"#F3E9D7\",\"beds\":[{{\"type\":\"OnePlaza\",\"x\":2,\"z\":2.5,\"rotationDeg\":0}}],\"windows\":[{{\"wall\":\"{windowWall}\",\"offset\":1,\"width\":1.2}}],\"door\":{{\"wall\":\"{doorWall}\",\"offset\":1.5,\"width\":0.9}}}}"
        );

        var scene = RoomVrSceneAssembler.ToVrScene(room);

        scene.Spawn.X.Should().BeApproximately(expectedX, 0.001);
        scene.Spawn.Z.Should().BeApproximately(expectedZ, 0.001);
        scene.Spawn.Y.Should().Be(1.6);
        scene.Spawn.YawDeg.Should().BeApproximately(expectedYaw, 0.001);
    }

    [Fact]
    public void ToVrScene_should_keep_spawn_inside_minimum_room()
    {
        var room = new Room();
        room.SetLayout("{\"widthM\":2,\"lengthM\":2,\"heightM\":2.8,\"floor\":\"Wood\",\"wallColor\":\"#F3E9D7\",\"beds\":[{\"type\":\"OnePlaza\",\"x\":1,\"z\":1,\"rotationDeg\":0}],\"windows\":[{\"wall\":\"N\",\"offset\":0.5,\"width\":0.8}],\"door\":{\"wall\":\"S\",\"offset\":0.5,\"width\":0.9}}"
        );

        var scene = RoomVrSceneAssembler.ToVrScene(room);

        // 2x2 con margen de 0.3 m => spawn debe permanecer dentro de [-0.7, +0.7] en cada eje.
        scene.Spawn.X.Should().BeGreaterThan(-0.7);
        scene.Spawn.X.Should().BeLessThan(0.7);
        scene.Spawn.Z.Should().BeGreaterThan(-0.7);
        scene.Spawn.Z.Should().BeLessThan(0.7);
        scene.Spawn.Y.Should().Be(1.6);
    }
}

