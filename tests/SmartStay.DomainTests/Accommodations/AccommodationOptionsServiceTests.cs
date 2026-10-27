using BackendAwSmartstay.API.Accommodations.Application.Internal.Services;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Entities;
using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace SmartStay.DomainTests.Accommodations;

public class AccommodationOptionsServiceTests
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
    [Trait("Area", "Accommodations")]
    [Trait("Risk", "BusinessRules")]
    public async Task CreateCategory_should_succeed_for_new_name()
    {
        // Arrange
        var repo = new FakeOptionsRepository();
        var uow = new FakeAccommodationsUnitOfWork();
        var commandService = new AccommodationOptionsCommandService(repo, uow);

        // Act
        var (success, error) = await commandService.CreateCategoryAsync("Boutique");

        // Assert
        success.Should().BeTrue();
        error.Should().BeNull();
        (await repo.CategoryExistsByNameAsync("Boutique")).Should().BeTrue();
    }

    [Fact]
    [Trait("Area", "Accommodations")]
    [Trait("Risk", "BusinessRules")]
    public async Task CreateCategory_should_fail_when_category_name_already_exists()
    {
        // Arrange
        var repo = new FakeOptionsRepository();
        await repo.AddCategoryAsync(new HotelCategory { Name = "Resort" });
        var uow = new FakeAccommodationsUnitOfWork();
        var commandService = new AccommodationOptionsCommandService(repo, uow);

        // Act
        var (success, error) = await commandService.CreateCategoryAsync("Resort");

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("already exists");
    }

    [Fact]
    [Trait("Area", "Accommodations")]
    [Trait("Risk", "BusinessRules")]
    public async Task CreateAmenity_should_succeed_and_prevent_duplicates()
    {
        // Arrange
        var repo = new FakeOptionsRepository();
        var uow = new FakeAccommodationsUnitOfWork();
        var commandService = new AccommodationOptionsCommandService(repo, uow);

        // Act 1
        var (firstSuccess, _) = await commandService.CreateAmenityAsync("Sauna");
        firstSuccess.Should().BeTrue();

        // Act 2
        var (secondSuccess, secondError) = await commandService.CreateAmenityAsync("Sauna");
        secondSuccess.Should().BeFalse();
        secondError.Should().Contain("already exists");
    }
}
