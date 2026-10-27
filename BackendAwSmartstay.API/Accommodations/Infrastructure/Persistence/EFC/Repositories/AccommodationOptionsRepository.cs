using BackendAwSmartstay.API.Accommodations.Domain.Model.Entities;
using BackendAwSmartstay.API.Accommodations.Domain.Repositories;
using BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Repositories;

/// <summary>
/// Implementación de persistencia para categorías y amenities sobre AccommodationsDbContext.
/// </summary>
public class AccommodationOptionsRepository(AccommodationsDbContext context) : IAccommodationOptionsRepository
{
    public async Task<IEnumerable<HotelCategory>> ListCategoriesAsync()
    {
        return await context.HotelCategories.ToListAsync();
    }

    public async Task<IEnumerable<Amenity>> ListAmenitiesAsync()
    {
        return await context.AmenitiesCatalog.ToListAsync();
    }

    public async Task<bool> CategoryExistsByNameAsync(string name)
    {
        return await context.HotelCategories.AnyAsync(c => c.Name == name);
    }

    public async Task<bool> AmenityExistsByNameAsync(string name)
    {
        return await context.AmenitiesCatalog.AnyAsync(a => a.Name == name);
    }

    public async Task AddCategoryAsync(HotelCategory category)
    {
        await context.HotelCategories.AddAsync(category);
    }

    public async Task AddAmenityAsync(Amenity amenity)
    {
        await context.AmenitiesCatalog.AddAsync(amenity);
    }
}
