using BackendAwSmartstay.API.Accommodations.Domain.Model.Entities;
using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using BackendAwSmartstay.API.Accommodations.Domain.Services;

namespace BackendAwSmartstay.API.Accommodations.Application.Internal.Services;

public class AccommodationOptionsQueryService(IAccommodationOptionsRepository repository) : IAccommodationOptionsQueryService
{
    public async Task<IEnumerable<string>> GetAllCategoriesAsync()
    {
        var categories = await repository.ListCategoriesAsync();
        return categories.Select(c => c.Name);
    }

    public async Task<IEnumerable<string>> GetAllAmenitiesAsync()
    {
        var amenities = await repository.ListAmenitiesAsync();
        return amenities.Select(a => a.Name);
    }
}

public class AccommodationOptionsCommandService(
    IAccommodationOptionsRepository repository,
    IAccommodationsUnitOfWork unitOfWork) : IAccommodationOptionsCommandService
{
    public async Task<(bool Success, string? ErrorMessage)> CreateCategoryAsync(string name)
    {
        if (await repository.CategoryExistsByNameAsync(name))
        {
            return (false, $"Category '{name}' already exists within the system constraints.");
        }

        var category = new HotelCategory { Name = name };
        await repository.AddCategoryAsync(category);
        await unitOfWork.CompleteAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? ErrorMessage)> CreateAmenityAsync(string name)
    {
        if (await repository.AmenityExistsByNameAsync(name))
        {
            return (false, $"Amenity '{name}' already exists within the system constraints.");
        }

        var amenity = new Amenity { Name = name };
        await repository.AddAmenityAsync(amenity);
        await unitOfWork.CompleteAsync();

        return (true, null);
    }
}
