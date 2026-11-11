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
    [InlineData("N", -0.15, 1.7, 180.0)]
    [InlineData("S", -0.15, -1.7, 0.0)]
    [InlineData("E", 1.7, -0.15, 270.0)]
    [InlineData("W", -1.7, -0.15, 90.0)]
    public void ToVrScene_should_place_spawn_inside_room_and_face_center(string doorWall, double expectedX, double expectedZ, double expectedYaw)
    {
        var room = new Room();
        room.SetLayout($"{{\"widthM\":4,\"lengthM\":5,\"heightM\":2.8,\"floor\":\"Wood\",\"wallColor\":\"#F3E9D7\",\"beds\":[{{\"type\":\"OnePlaza\",\"x\":2,\"z\":2.5,\"rotationDeg\":0}}],\"windows\":[{{\"wall\":\"N\",\"offset\":1,\"width\":1.2}}],\"door\":{{\"wall\":\"{doorWall}\",\"offset\":1.5,\"width\":0.9}}}}"
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

        scene.Spawn.X.Should().BeGreaterThan(-1.0);
        scene.Spawn.X.Should().BeLessThan(1.0);
        scene.Spawn.Z.Should().BeGreaterThan(-1.0);
        scene.Spawn.Z.Should().BeLessThan(1.0);
        scene.Spawn.Y.Should().Be(1.6);
    }
}
