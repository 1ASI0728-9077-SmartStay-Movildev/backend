using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Entities;
using BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Configuration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SmartStay.DomainTests.Accommodations;

public class AccommodationsModelBoundaryTests
{
    [Fact]
    [Trait("Area", "Accommodations")]
    [Trait("Risk", "ArchitectureBoundary")]
    public void AccommodationsDbContext_model_must_only_contain_accommodations_entities()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AccommodationsDbContext>()
            .UseInMemoryDatabase(databaseName: "accommodations_boundary_test")
            .Options;

        using var context = new AccommodationsDbContext(options);
        var entityTypeNames = context.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToList();

        // Assert expected entities are present
        entityTypeNames.Should().Contain(nameof(Hotel));
        entityTypeNames.Should().Contain(nameof(Room));
        entityTypeNames.Should().Contain(nameof(RoomType));
        entityTypeNames.Should().Contain(nameof(HotelCategory));
        entityTypeNames.Should().Contain(nameof(Amenity));

        // Assert strictly forbidden foreign bounded context aggregates are NOT in AccommodationsDbContext
        entityTypeNames.Should().NotContain("Booking", "AccommodationsDbContext must not know about Bookings");
        entityTypeNames.Should().NotContain("Payment", "AccommodationsDbContext must not know about Payments");
        entityTypeNames.Should().NotContain("User", "AccommodationsDbContext must not know about IAM Users");
        entityTypeNames.Should().NotContain("Profile", "AccommodationsDbContext must not know about Profiles");
    }

    [Fact]
    [Trait("Area", "Accommodations")]
    [Trait("Risk", "DomainInvariants")]
    public void Room_must_require_positive_price_and_belong_to_hotel()
    {
        // Arrange
        var command = new BackendAwSmartstay.API.Accommodations.Domain.Model.Commands.CreateRoomCommand(
            HotelId: 1,
            RoomTypeId: 1,
            Price: 149.99m,
            Description: "Ocean view deluxe room",
            Amenities: ["Wifi", "Minibar"]
        );
        var room = new Room(command);

        // Assert
        room.Price.Should().BeGreaterThan(0);
        room.HotelId.Should().Be(1);
        room.Amenities.Should().Contain("Wifi");
    }
}
