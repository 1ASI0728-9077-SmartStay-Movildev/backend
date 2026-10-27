using BackendAwSmartstay.API.Accommodations.Application.Internal.Services;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Commands;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Entities;
using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using SmartStay.Accommodations.Infrastructure.Persistence.EFC.Configuration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SmartStay.Accommodations.Tests;

public class AccommodationsDomainAndBoundaryTests
{
    private class FakeOptionsRepository : IAccommodationOptionsRepository
    {
        private readonly List<HotelCategory> _categories = [];
        private readonly List<Amenity> _amenities = [];

        public Task<IEnumerable<HotelCategory>> ListCategoriesAsync() => Task.FromResult<IEnumerable<HotelCategory>>(_categories);
        public Task<IEnumerable<Amenity>> ListAmenitiesAsync() => Task.FromResult<IEnumerable<Amenity>>(_amenities);

        public Task<bool> CategoryExistsByNameAsync(string name)
            => Task.FromResult(_categories.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

        public Task<bool> AmenityExistsByNameAsync(string name)
            => Task.FromResult(_amenities.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

        public Task AddCategoryAsync(HotelCategory category)
        {
            _categories.Add(category);
            return Task.CompletedTask;
        }

        public Task AddAmenityAsync(Amenity amenity)
        {
            _amenities.Add(amenity);
            return Task.CompletedTask;
        }
    }

    private class FakeAccommodationsUnitOfWork : IAccommodationsUnitOfWork
    {
        public Task CompleteAsync() => Task.CompletedTask;
    }

    [Fact]
    [Trait("Service", "Accommodations")]
    [Trait("Category", "BusinessRules")]
    public async Task CreateCategory_should_succeed_for_new_name()
    {
        var repo = new FakeOptionsRepository();
        var uow = new FakeAccommodationsUnitOfWork();
        var commandService = new AccommodationOptionsCommandService(repo, uow);

        var (success, error) = await commandService.CreateCategoryAsync("Boutique");

        success.Should().BeTrue();
        error.Should().BeNull();
        (await repo.CategoryExistsByNameAsync("Boutique")).Should().BeTrue();
    }

    [Fact]
    [Trait("Service", "Accommodations")]
    [Trait("Category", "BusinessRules")]
    public async Task CreateCategory_should_fail_when_category_name_already_exists()
    {
        var repo = new FakeOptionsRepository();
        await repo.AddCategoryAsync(new HotelCategory { Name = "Resort" });
        var uow = new FakeAccommodationsUnitOfWork();
        var commandService = new AccommodationOptionsCommandService(repo, uow);

        var (success, error) = await commandService.CreateCategoryAsync("Resort");

        success.Should().BeFalse();
        error.Should().Contain("already exists");
    }

    [Fact]
    [Trait("Service", "Accommodations")]
    [Trait("Category", "BusinessRules")]
    public async Task CreateAmenity_should_succeed_and_prevent_duplicates()
    {
        var repo = new FakeOptionsRepository();
        var uow = new FakeAccommodationsUnitOfWork();
        var commandService = new AccommodationOptionsCommandService(repo, uow);

        var (firstSuccess, _) = await commandService.CreateAmenityAsync("Sauna");
        firstSuccess.Should().BeTrue();

        var (secondSuccess, secondError) = await commandService.CreateAmenityAsync("Sauna");
        secondSuccess.Should().BeFalse();
        secondError.Should().Contain("already exists");
    }

    [Fact]
    [Trait("Service", "Accommodations")]
    [Trait("Category", "ArchitectureBoundary")]
    public void AccommodationsDbContext_model_must_only_contain_accommodations_entities()
    {
        var options = new DbContextOptionsBuilder<AccommodationsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new AccommodationsDbContext(options);
        var entityTypeNames = context.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToList();

        entityTypeNames.Should().Contain(nameof(Hotel));
        entityTypeNames.Should().Contain(nameof(Room));
        entityTypeNames.Should().Contain(nameof(RoomType));
        entityTypeNames.Should().Contain(nameof(HotelCategory));
        entityTypeNames.Should().Contain(nameof(Amenity));

        entityTypeNames.Should().NotContain("Booking");
        entityTypeNames.Should().NotContain("Payment");
        entityTypeNames.Should().NotContain("User");
        entityTypeNames.Should().NotContain("Profile");
    }

    [Fact]
    [Trait("Service", "Accommodations")]
    [Trait("Category", "DomainInvariants")]
    public void Room_must_require_positive_price_and_belong_to_hotel()
    {
        var command = new CreateRoomCommand(
            HotelId: 1,
            RoomTypeId: 1,
            Price: 149.99m,
            Description: "Ocean view deluxe room",
            Amenities: ["Wifi", "Minibar"]
        );
        var room = new Room(command);

        room.Price.Should().BeGreaterThan(0);
        room.HotelId.Should().Be(1);
        room.Amenities.Should().Contain("Wifi");
    }

    [Fact]
    [Trait("Service", "Accommodations")]
    [Trait("Category", "DomainInvariants")]
    public void Room_supports_capacity_currency_and_tour_path()
    {
        var command = new CreateRoomCommand(
            HotelId: 1,
            RoomTypeId: 2,
            Price: 150.00m,
            Description: "Double deluxe room",
            Amenities: ["Wifi", "Minibar"],
            Capacity: 2,
            Currency: "USD",
            IsBookable: true,
            TourPath: "/tours/room-102/manifest.json"
        );
        var room = new Room(command);

        room.Capacity.Should().Be(2);
        room.Currency.Should().Be("USD");
        room.IsBookable.Should().BeTrue();
        room.TourPath.Should().Be("/tours/room-102/manifest.json");
    }
}
